# 在线升级架构

- 状态：已实现（`v1.0.0`）
- 适用范围：Docker Compose 单实例部署

## 1. 目标

管理员可以在系统页面：

1. 检查 GitHub Release 中的新稳定版本；
2. 查看版本说明、兼容性和升级前检查；
3. 重新验证当前密码后发起升级；
4. 观察下载、备份、迁移、健康检查和回滚状态；
5. 升级失败时自动恢复旧 App 镜像和升级前 SQLite 备份。

首版在线升级只替换 App。Updater、Compose、端口、卷、权限或其他部署契约变化时，页面只展示说明和主机升级命令，不在线替换 Updater，也不修改 Compose。

在线升级不能演变成通用 Docker 管理功能，也不能允许页面指定任意 URL、镜像、命令或容器。

## 2. 发布与分发模型

源码存放在公开 GitHub 仓库。生产服务器不从公共容器 Registry 拉取镜像。Tag `vX.Y.Z` 触发 GitHub Actions，构建镜像后使用 `docker save` 导出并压缩，通过 GitHub Release Assets 发布。

每个 Release 包含：

```text
sub2api-report-v1.2.0-linux-amd64.tar.gz
sub2api-report-app-v1.2.0-linux-amd64.tar.gz
sub2api-report-updater-v1.2.0-linux-amd64.tar.gz
release-manifest.json
release-manifest.sig
checksums.txt
CHANGELOG.md
LICENSE
release-notes-v1.2.0.md
sub2api-report-app-v1.2.0.spdx.json
sub2api-report-updater-v1.2.0.spdx.json
```

完整 bundle 用于首次安装和手工部署契约升级。普通在线升级只下载 App 镜像归档、manifest 和签名。

GitHub Actions artifact 只用于 Job 间传递，不能作为安装或升级下载源；最终制品必须进入 GitHub Release Assets。

## 3. 安全边界

Docker Engine Socket 等价于宿主机 root 权限，因此：

- App 绝不挂载 `/var/run/docker.sock`；
- 只有专用 Updater 可以访问 Docker Engine；
- Updater 不映射主机端口，只存在于 Compose 私有网络；
- App 和 Updater 使用安装时生成的共享 token；
- Updater 只接受固定的 check、plan、install 和 status 动作；
- Updater 只管理带当前 instance ID 和固定 role 标签的 App 容器；
- Updater 只下载固定 GitHub owner/repository 的 HTTPS Release 路径；
- manifest 签名、版本、架构、归档哈希和部署契约全部通过后才允许 `docker load`；
- 只有通过 [Updater 威胁模型](updater-threat-model.md) 中的签名、权限和故障验收后，官方 Compose 才挂载 Docker Socket 并启用安装。

Updater 仍是高权限组件，必须保持很小的 API 和代码边界。App 被攻破后不能借助 Updater 指定任意 URL、加载任意镜像、执行任意 Docker API 或修改宿主机文件。

## 4. 组件模型

```text
Browser
  -> App /api/v1/updates/*
  -> step-up authorization
  -> Updater internal API
  -> verify signed release manifest
  -> download and verify App image archive
  -> docker load
  -> backup, replace, verify or rollback App
```

Updater 常驻并将操作状态持久化到 `/update-state`。它不在线替换自身。若 manifest 要求更高的 Updater 或 deployment contract，返回 `manual_upgrade_required`。

## 5. Release manifest

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
  "manualUpgradeRequired": false,
  "onlineInstallSupported": true,
  "signatureAlgorithm": "RSASSA-PKCS1-v1_5-SHA256",
  "app": {
    "archiveUrl": "https://github.com/example/sub2api-report/releases/download/v1.2.0/sub2api-report-app-v1.2.0-linux-amd64.tar.gz",
    "archiveSha256": "<sha256>",
    "imageId": "sha256:<image-id>",
    "loadedTag": "sub2api-report-app:1.2.0",
    "size": 123456789
  },
  "updater": {
    "archiveUrl": "https://github.com/example/sub2api-report/releases/download/v1.2.0/sub2api-report-updater-v1.2.0-linux-amd64.tar.gz",
    "archiveSha256": "<sha256>",
    "imageId": "sha256:<image-id>",
    "loadedTag": "sub2api-report-updater:1.2.0",
    "size": 45678901,
    "selfUpdateSupported": false
  },
  "database": {
    "targetMigration": "20260826000000_ExampleMigration",
    "requiresBackupRestoreForRollback": true
  },
  "releaseNotes": {
    "pageUrl": "https://github.com/example/sub2api-report/releases/tag/v1.2.0",
    "assetUrl": "https://github.com/example/sub2api-report/releases/download/v1.2.0/release-notes-v1.2.0.md",
    "sha256": "<sha256>",
    "size": 12345
  }
}
```

Updater 从安装目录只读挂载的发布公钥建立本地信任锚并校验：

- manifest schema；
- 签名；
- stable channel；
- SemVer 高于当前版本；
- `linux/amd64`；
- 固定 GitHub owner/repository 和 Release URL 结构；
- archive SHA-256、大小上限和文件名；
- 当前版本 Release notes 的 SHA-256 和大小；
- deployment contract；
- minimum updater version；
- target database schema；
- 禁止跨越必须手工处理的版本。

GitHub API 和 Release 内容不能覆盖本地公钥、host allowlist、版本规则或部署契约。

## 6. 部署契约

`deploymentContractVersion` 定义：

- App 容器角色和 instance ID 标签；
- 数据卷挂载点 `/data`；
- 内部端口 `8080`；
- health endpoint；
- 必需环境变量；
- Compose 网络；
- updater token 路径；
- 本地镜像标签 `sub2api-report-app:current`；
- 容器替换和回滚协议。

同一契约内，Updater 从当前 App 容器读取并验证端口、卷、环境变量、网络、restart policy、安全选项和资源限制，再用新 image ID 重建 App。

下列情况必须手工升级完整 bundle：

- deployment contract 提升；
- Updater 版本不足；
- Updater 镜像变化需要生效；
- Compose、端口、卷、网络或安全权限变化。

手工 `update.sh` 保留 `.env`、token、instance ID、数据卷和运行数据。

## 7. 升级状态机

```text
Idle
 -> Checking
 -> UpdateAvailable
 -> Preflight
 -> DownloadingArchive
 -> VerifyingArchive
 -> LoadingImage
 -> BackingUp
 -> Maintenance
 -> ReplacingApp
 -> Migrating
 -> Verifying
 -> Succeeded

任一可恢复阶段
 -> RollingBack
 -> RolledBack

不可恢复错误
 -> FailedNeedsOperator
```

状态写入 `/update-state/operations/<operation-id>.json`。浏览器断开或 App 重启不会丢失结果。状态文件使用原子写入，不包含凭证、报告内容或 GitHub token。

## 8. 升级前检查

Updater 开始安装前检查：

- 没有另一个升级任务；
- 没有报告处于 Collecting、Rendering 或 Delivering；
- manifest 和签名有效；
- 版本、Updater 和 deployment contract 兼容；
- 主机和归档均为 amd64；
- Docker Engine 可用；
- 数据卷可写；
- 下载、解压、镜像加载、数据库备份和旧镜像保留空间充足；
- 当前 App `/health/ready` 正常；
- 最近有效备份可读取；
- 目标 archive hash 未在本地拒绝列表中。

警告项需要二次确认，不可恢复项禁止安装。

## 9. 下载和加载镜像

Updater：

1. 只向固定 GitHub Release URL 发起 HTTPS 请求；
2. 禁止重定向到 host allowlist 之外；
3. 使用流式下载并限制总大小和超时；
4. 将归档写入 `/update-state/downloads/<operation-id>.partial`；
5. 计算并校验 SHA-256 后原子改名；
6. 调用 Docker API 加载归档，不执行 shell `docker load` 命令；
7. 校验加载后的 OS、architecture、image ID、版本 label 和预期 tag；
8. 将新 image ID 记录到持久化操作状态。

下载和镜像加载阶段不影响当前 App。镜像加载失败时不修改 `current` 标签或容器。

## 10. 一致性备份和维护模式

Updater 请求 App 进入 `PreparingMaintenance`：

1. 停止接收新的报告和发送任务；
2. 等待活动任务安全结束；
3. checkpoint SQLite WAL；
4. 使用 SQLite Backup API 写入临时备份；
5. 对备份执行 `PRAGMA integrity_check`；
6. 计算 SHA-256；
7. 原子重命名为正式升级备份。

备份成功后 App 进入维护模式：

- readiness 返回失败；
- 普通 API 返回 `503` Problem Details；
- health 和内部升级端点继续可用；
- 前端展示升级进度并停止提交表单。

备份失败则退出维护准备，当前 App 保持运行。

## 11. 替换和验证 App

Updater：

1. 记录旧容器配置和旧 image ID；
2. 停止旧 App，但暂不删除旧 image；
3. 将目标镜像标记为本地 `sub2api-report-app:current`；
4. 按已验证的当前 deployment contract 重建 App；
5. 设置 upgrade operation label；
6. 启动候选 App。

候选 App entrypoint 先运行 Migrator，成功后启动 Web。候选 App 在升级验证完成前保持维护模式，不能开放业务写入。

Updater 在 120 秒默认窗口内要求连续成功：

- 容器没有退出或反复重启；
- `/health/live` 成功；
- `/health/ready` 成功；
- version、schema 和 deployment contract 与 manifest 一致；
- SQLite quick check 成功；
- SPA 主文件可读取；
- 内部 updater token 握手成功。

Sub2API、SMTP、钉钉和飞书连通性不作为升级成功条件。

成功后解除维护模式、删除旧 App 容器、保留最近旧 image ID 和升级备份，并写 UpdateRecord 与审计事件。

## 12. 自动回滚

下列情况触发回滚：

- Migrator 失败；
- 候选 App 退出；
- readiness 超时；
- version/schema/contract 不匹配；
- SQLite quick check 失败；
- 内部 token 握手失败。

回滚步骤：

1. 停止并移除候选 App；
2. 隔离升级失败后的数据库文件；
3. 从已校验的升级前备份恢复 SQLite；
4. 将旧 image ID 恢复为 `sub2api-report-app:current`；
5. 按原容器配置重建旧 App；
6. 验证旧版本 readiness；
7. 记录失败阶段和恢复结果；
8. 将目标 archive SHA-256 加入临时拒绝列表。

若旧版本也无法 ready，状态为 `FailedNeedsOperator`。Updater 保留两个数据库文件、备份、镜像 ID 和状态记录，不继续清理，并输出固定的主机恢复命令。

自动回滚只发生在候选版本尚未开放业务写入的验证阶段。管理员之后手工回滚必须明确提示会恢复升级前快照并丢弃升级后的新配置和执行记录。

## 13. 数据库迁移规则

- 每个 Release 声明目标 schema version；
- migration 必须在数据库副本上通过 CI；
- Web 进程不直接调用 EF migration；
- 独立 Migrator 获取 migration lock；
- migration 失败只输出脱敏摘要；
- 成功后执行 quick check；
- App 在迁移和验证完成前不运行 Quartz Job；
- 回滚恢复整库备份，不依赖 down migration。

## 14. 页面交互

页面显示当前/目标版本、发布时间、Release notes、下载大小、预计停机、schema 变化、部署契约、备份状态、preflight、当前阶段和升级历史。

“安装更新”要求 step-up。若 `manualUpgradeRequired=true`，页面不显示在线安装按钮，只显示完整 bundle 下载链接和固定主机命令。

浏览器只轮询 App API。App 重启期间使用退避重连，不直接访问 Updater，也不持有共享 token。

## 15. GitHub Actions 发布流程

Tag `vX.Y.Z` 触发：

1. 校验版本、Tag 和 `CHANGELOG.md` 对应版本章节；
2. 提取当前版本 Release notes；
3. 运行 .NET 和前端完整质量门；
4. 构建 linux/amd64 App 和 Updater 镜像；
5. 扫描漏洞并生成 SBOM；
6. 使用 `docker save` 和 gzip 生成离线镜像归档；
7. 读取 image ID、架构、版本 label 和归档 SHA-256；
8. 生成包含 Release notes 哈希的 release manifest；
9. 使用独立发布密钥签名 manifest；
10. 为归档、manifest、变更日志和 bundle 生成 GitHub artifact attestations；
11. 组装完整 deploy bundle 和 checksums；
12. 使用提取的版本说明创建 draft GitHub Release 并上传 Assets；
13. 人工审核后发布并标记 Latest；已发布 Assets 不覆盖，修正使用新版本。

PR Job 不能读取发布签名秘密。在线安装启用前必须完成签名密钥托管和轮换演练。

## 16. 故障注入测试

上线安装按钮前自动覆盖：

- manifest 签名错误；
- 非 allowlist Release URL 或 redirect；
- 归档 hash、大小、架构、image ID 或 label 不匹配；
- 下载和 Docker load 中断；
- 磁盘不足；
- SQLite 备份或 integrity check 失败；
- migration 中断；
- 新 App 立即退出或 readiness 永不成功；
- Docker daemon 短时不可用；
- 浏览器关闭；
- 报表运行中请求升级；
- Updater 进程中止后恢复未完成状态；
- 回滚后数据库哈希和旧版本健康检查。

测试只使用隔离的临时目录、一次性 SQLite 和合成数据。

## 17. 已知限制和验收

- 不支持 rootless Docker、Podman、Swarm 和 Kubernetes；
- 首版只支持项目提供的 Compose 契约和 linux/amd64；
- 不实现 Updater 在线自更新；
- 跨 deployment contract 必须手工执行新 bundle 的 `update.sh`；
- 主机磁盘或 Docker daemon 故障可能需要操作员介入；
- App 容器中不存在 Docker Socket；
- Updater 无主机端口并拒绝未知路由和字段；
- 篡改 manifest、归档或预期镜像信息时升级被拒绝；
- 一键升级后配置、报告和登录状态保留；
- migration 或健康检查失败时旧 App 和升级前数据库自动恢复；
- Updater 中止后可以继续判断并恢复事务；
- 全过程具有脱敏日志、审计事件和页面状态。
