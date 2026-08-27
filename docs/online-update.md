# 在线升级架构

- 状态：设计基线
- 适用范围：Docker Compose 单实例部署

## 1. 目标

管理员可以在系统页面：

1. 检查 GitHub 上的新稳定版本；
2. 查看版本说明、兼容性和升级前检查；
3. 重新验证当前密码后发起升级；
4. 在页面观察下载、备份、迁移、健康检查和回滚状态；
5. 升级失败时自动恢复旧应用镜像和升级前 SQLite 备份。

在线升级不能演变成通用 Docker 管理功能，也不能允许页面指定任意镜像、命令或容器。

## 2. 安全前提

Docker Engine Socket 等价于宿主机 root 权限。基于这一事实：

- 主应用容器绝不挂载 `/var/run/docker.sock`；
- 只有专用 updater 组件可以访问 Docker Engine；
- updater 不映射任何主机端口；
- updater API 只存在于 Compose 私有网络；
- updater 只接受固定升级动作和固定项目标签；
- updater 只允许官方 GHCR 仓库和经过签名的版本；
- 应用即使被攻破，也不能通过 updater 执行任意 Docker API。

这仍然是一个高权限组件。公开项目前必须对 updater 做独立代码审计和端到端故障注入测试。

## 3. 组件模型

同一个 updater 镜像提供两种运行模式：

```text
controller mode
  - 常驻
  - 内部 API：check / plan / install / status
  - 验证请求、签名和兼容性
  - 创建一次性 update worker

worker mode
  - 仅升级时存在
  - 获取全局升级锁
  - 备份、替换、迁移、健康检查、回滚
  - 更新 app 和 controller
  - 完成后退出并由 Docker 自动删除
```

数据流：

```text
Browser
  -> App /api/v1/updates/install
  -> verify admin step-up authorization
  -> Controller internal API
  -> verify signed release manifest
  -> launch target-version update worker
  -> worker replaces App
  -> worker replaces Controller when required
  -> worker exits
```

App 和 Controller 使用安装时生成的随机共享 token 进行内部认证。该 token 通过 Compose Secret 文件挂载，不出现在环境变量、日志或数据库中。

## 4. 发布物

每个 GitHub Release 包含：

- `ghcr.io/<owner>/sub2api-report-app:<semver>`；
- `ghcr.io/<owner>/sub2api-report-updater:<semver>`；
- 两个镜像的 immutable digest；
- `release-manifest.json`；
- `release-manifest.sig`；
- amd64 SBOM；
- GitHub artifact attestation；
- Release notes；
- 首次安装包 `deploy-bundle.tar.gz` 及 checksum。

运行时永远按 digest 拉取，不依赖可变的 `latest` 或 `stable` tag。

### 4.1 Release manifest

示例只展示公开字段：

```json
{
  "schemaVersion": 1,
  "version": "1.2.0",
  "channel": "stable",
  "publishedAt": "2026-08-26T08:00:00Z",
  "architecture": "linux/amd64",
  "deploymentContractVersion": 1,
  "minimumUpdaterVersion": "1.0.0",
  "app": {
    "repository": "ghcr.io/example/sub2api-report-app",
    "digest": "sha256:<digest>"
  },
  "updater": {
    "repository": "ghcr.io/example/sub2api-report-updater",
    "digest": "sha256:<digest>"
  },
  "database": {
    "targetSchemaVersion": 12,
    "requiresBackupRestoreForRollback": true
  },
  "releaseNotesUrl": "https://github.com/example/sub2api-report/releases/tag/v1.2.0"
}
```

Updater 内置发布公钥，先验证 manifest 签名，再校验：

- schema version；
- stable channel；
- SemVer 必须高于当前版本；
- `linux/amd64`；
- 镜像仓库固定 allowlist；
- digest 格式；
- deployment contract；
- minimum updater version；
- 禁止跨越被标记为必须手工处理的版本。

GitHub/GHCR 返回内容不能覆盖上述本地安全策略。

## 5. 部署契约

`deploymentContractVersion` 定义 App 容器的稳定运行契约：

- 容器角色标签；
- 数据卷挂载点 `/data`；
- 内部端口 `8080`；
- health endpoint；
- 必需环境变量；
- Compose 私有网络；
- updater token secret 路径；
- 容器替换和回滚协议。

同一契约版本内，worker 可以检查当前容器配置并只替换 image digest，保留端口、卷、环境变量、网络、restart policy 和资源限制。

如果新版本提高 `deploymentContractVersion`，页面只展示更新说明和手工升级命令，不执行在线升级。这样避免 updater 猜测新的卷、网络或权限配置。

## 6. 升级状态机

```text
Idle
 -> Checking
 -> UpdateAvailable
 -> Preflight
 -> PullingWorker
 -> BackingUp
 -> Maintenance
 -> PullingImages
 -> ReplacingApp
 -> Migrating
 -> Verifying
 -> ReplacingController (optional)
 -> Succeeded

任一可恢复阶段
 -> RollingBack
 -> RolledBack

不可恢复错误
 -> Failed
```

状态持久化在 updater 自己的 `/update-state` volume 中。浏览器断开、App 重启或 Controller 交接不会丢失当前升级结果。

## 7. 升级前检查

Controller 在启动 worker 前检查：

- 当前没有另一个升级任务；
- 当前没有报表处于 `Collecting/Rendering/Delivering`；
- Release manifest 和签名有效；
- 版本和部署契约兼容；
- 宿主机架构为 amd64；
- Docker Engine 可用；
- 数据卷可写；
- 数据卷和镜像存储空间满足最低要求；
- 当前 App `/health/ready` 正常；
- 最近一次有效备份可读取；
- 目标镜像 digest 尚未被本地拒绝列表标记。

管理员界面展示检查结果。不可恢复项禁止点击升级，警告项需要二次确认。

## 8. 升级执行流程

### 8.1 获取锁

Worker 创建带租约的全局升级锁。锁包含 operation ID、worker container ID、开始时间和心跳。

只有在原 worker 不存在且锁超时后才能恢复锁，不能仅按时间无条件抢锁。

### 8.2 拉取目标镜像

- 目标 worker 本身按 manifest 中的 updater digest 启动；
- 拉取 App digest；
- 校验本地镜像实际 digest；
- 可选验证 GitHub artifact attestation；
- 不接受 registry redirect 到非 allowlist host。

下载阶段不影响当前 App 服务。

### 8.3 创建一致性备份

Worker 请求 App 进入 `PreparingMaintenance`：

1. 停止接收新的报表和发送任务；
2. 等待活动任务安全结束；
3. checkpoint SQLite WAL；
4. 使用 SQLite backup API 写入临时备份；
5. 对备份执行 `PRAGMA integrity_check`；
6. 计算 SHA-256；
7. 原子重命名为正式升级备份。

备份元数据：

```text
operationId, fromVersion, targetVersion, schemaVersion,
createdAt, databaseFile, sha256, integrityCheck
```

备份失败则升级立即终止，当前 App 保持运行。

### 8.4 进入维护模式

App 切换到维护模式：

- readiness 返回失败；
-普通 API 返回 `503` Problem Details；
-健康和内部升级端点仍可访问；
-前端展示升级进度并停止提交表单。

随后 worker 停止旧 App，但保留旧容器和旧 image digest 作为回滚对象。

### 8.5 启动候选 App

Worker：

1. 将旧容器重命名为带 operation ID 的备份名称；
2. 按当前 deployment contract 创建目标 App 容器；
3. 保留原数据卷、网络、环境、端口、restart policy 和安全配置；
4. 设置目标 image digest 和 upgrade operation label；
5. 启动候选容器。

候选容器 entrypoint 先运行 `Sub2ApiReport.Migrator`，成功后才启动 Web 进程。

### 8.6 健康验证

Worker 在限定时间内检查：

- 容器没有退出或反复重启；
- `/health/live` 成功；
- `/health/ready` 成功；
- App 返回的 version 和 schema version 与 manifest 一致；
- SQLite quick check 成功；
- 静态 SPA 主文件可读取；
- 内部 updater token 握手成功。

健康检查默认超时 120 秒，使用连续成功阈值而不是单次成功。

不把 Sub2API、SMTP、钉钉或飞书连通性作为升级成功条件。

### 8.7 Controller 交接

若 updater digest 有变化：

1. Worker 创建新 Controller 容器并使用临时内部别名；
2. 新 Controller 加载 update state，完成自检；
3. Worker 停止并移除旧 Controller；
4. 新 Controller 获得稳定网络别名；
5. App 的内部状态检查确认新 Controller 可达。

Worker 完成后退出并自动删除。新 Controller 写入最终成功状态。

如果 updater 未变化，跳过该步骤。

### 8.8 完成

- App 退出维护模式；
- 将新版本标记为当前稳定版本；
- 保留旧镜像和数据库备份；
- 删除旧 App 容器；
- 写入 `UpdateRecord` 和审计事件；
- 前端重载并显示 Release notes。

## 9. 自动回滚

以下情况触发回滚：

- Migrator 失败；
- 候选容器退出；
- readiness 超时；
- version/schema 不匹配；
- SQLite integrity check 失败；
- Controller 交接失败且不能恢复稳定 updater。

回滚步骤：

1. 停止并移除候选 App；
2. 隔离升级失败后的数据库文件；
3. 从校验通过的升级前备份恢复 SQLite；
4. 将旧 App 容器恢复原名称和网络别名；
5. 启动旧 App；
6. 验证旧版本 ready；
7. 记录失败阶段、错误摘要和恢复结果；
8. 将目标 digest 加入本地临时拒绝列表，避免重复自动尝试。

若旧版本也无法 ready，状态标记为 `FailedNeedsOperator`，保留所有备份和容器信息，不继续做破坏性清理，并在日志输出明确的主机恢复命令。

## 10. 数据库迁移规则

SQLite 回滚依赖升级前备份，因此：

- 每次 release 必须声明目标 schema version；
- migration 必须可在数据库副本上通过 CI 测试；
- 禁止 Web 进程在正常启动中隐式迁移；
- Migrator 获取 EF migration lock；
- migration 失败要输出不含业务数据的错误摘要；
- 成功后执行 quick check；
- App 在迁移和验证完成前不运行 Quartz Job；
- 回滚必须恢复整库备份，不能假设 down migration 完整可靠。

一个版本迁移完成并对外提供服务后，管理员仍可手工回滚，但必须明确提示会恢复升级前快照并丢弃升级后的新配置和运行记录。自动回滚只发生在候选版本尚未开放写操作的验证阶段。

## 11. 页面交互

更新页面显示：

- 当前版本、目标版本、发布时间和发布通道；
- Release notes；
- 镜像大小和预计停机时间；
- 数据库 migration 信息；
- 部署契约兼容性；
- 最近备份状态；
- 升级前检查列表；
- 实时阶段、开始时间和错误摘要；
- 升级历史。

“安装更新”是明确命令按钮，使用下载图标；点击后打开确认 Dialog，要求输入当前密码。危险或不可回滚提示使用文字和图标，不能只靠颜色。

浏览器只轮询 App API。App 重启期间，前端按退避策略检查恢复状态，不直接访问 Controller，也不持有 updater token。

## 12. GitHub Actions 发布流程

Tag `vX.Y.Z` 触发：

1. 校验版本、工作树和 changelog；
2. 运行 .NET 和前端测试；
3. 构建 linux/amd64 App 和 Updater 镜像；
4. 生成 SBOM 和漏洞扫描报告；
5. 推送到 GHCR；
6. 获取 immutable digest；
7. 生成 release manifest；
8. 使用发布密钥签名 manifest；
9. 生成 GitHub artifact attestations；
10. 组装 deploy bundle 和 checksum；
11. 创建 draft GitHub Release；
12. 人工审核后发布 immutable release。

Actions 权限最小化：

```yaml
permissions:
  contents: read
  packages: write
  attestations: write
  id-token: write
```

发布 Job 与 PR Job 分离，PR 代码不能直接读取发布签名秘密。

## 13. 故障注入测试

上线在线升级前必须自动覆盖：

- manifest 签名错误；
- 非 allowlist 镜像；
- 镜像下载中断；
- 磁盘不足；
- SQLite 备份或 integrity check 失败；
- migration 中断；
- 新 App 立即退出；
- readiness 永不成功；
- Controller 交接时旧 Controller 异常退出；
- Worker 中途被 kill 后恢复；
- Docker daemon 短时不可用；
- 浏览器在升级期间关闭；
- 报表运行期间请求升级；
- 回滚恢复后数据库哈希和旧版本健康检查。

测试不能使用生产数据库、生产凭证或真实报告。

## 14. 已知限制

- Docker Socket 风险不能被“内网”完全消除；只能通过组件隔离、固定操作和审计降低风险。
- 不支持 rootless Docker、Podman 和 Docker Swarm，除非后续单独验证。
- 只支持项目提供的 Docker Compose 部署契约。
- 跨 deployment contract 的升级需要执行 GitHub Release 中的主机命令。
- 主机磁盘或 Docker daemon 本身故障无法由应用内回滚解决。
- 首版只检查稳定版，不自动安装，也不支持降级到任意历史版本。

## 15. 验收标准

- App 容器中不存在 Docker Socket。
- Controller 无主机端口，拒绝所有未知路由和未知字段。
- 篡改 manifest、digest 或仓库地址时升级被拒绝。
- 一键升级成功后配置、报告和登录状态按预期保留。
- migration 或健康检查失败时，旧版本和升级前数据库自动恢复。
- Worker 异常中止后系统能够判断并恢复未完成事务。
- 升级过程和结果有脱敏日志、审计事件和页面状态。
- 所有升级测试只使用公开的合成 fixture。
