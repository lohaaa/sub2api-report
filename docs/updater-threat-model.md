# Updater 威胁模型

- 状态：`v1.0.0` 发布门
- 范围：官方 linux/amd64 Docker Compose 单实例部署

## 1. 信任边界

Updater 可以访问 Docker Engine Socket，因此等价于宿主机高权限组件。App 和浏览器均不接触 Docker Socket。

信任锚只有安装目录中的只读发布公钥。GitHub API、Release manifest、App 请求和浏览器输入都不能修改公钥、仓库 allowlist、实例 ID 或 deployment contract。

```text
Browser -> authenticated App API -> private control network -> Updater
Updater -> fixed GitHub repository / Release hosts
Updater -> Docker Engine Socket
Updater -> app-data (SQLite source) + updater-state (backup/state)
```

## 2. 受保护资产

- SQLite 数据库、Data Protection key ring 和报告快照；
- Updater shared token 和发布私钥；
- Docker Engine 与当前实例容器；
- 签名 Release manifest、镜像归档和回滚备份；
- 管理员会话和升级审计结果。

发布私钥只存在于维护者的仓库外密钥目录和 GitHub Actions Secret，不进入镜像、bundle、日志或状态文件。

## 3. 主要威胁与控制

| 威胁 | 控制 |
| --- | --- |
| App 被攻破后调用任意 Docker API | Updater 只暴露固定 check/plan/install/status；请求不能提交 URL、镜像或命令 |
| 未授权进程访问 Updater | 私有 control network、无主机端口、64-hex shared token、常量时间比较、缺失时 fail closed |
| GitHub API 或 Release 被篡改 | 固定 owner/repository、HTTPS、redirect host allowlist、RSA 签名、严格 JSON、SHA-256、大小上限 |
| 加载错误架构或伪造镜像 | 校验 image ID、linux/amd64、OCI version、role/contract label 和 loaded tag |
| 操作其他项目容器 | 必须同时匹配 `io.sub2api-report.role=app` 和安装生成的 instance ID；多匹配直接拒绝 |
| 路径穿越或任意文件下载 | 资产名和 Release 路径精确匹配；下载只写 updater-state；临时文件原子完成 |
| 并发或重放安装 | 持久化 operation、单槽队列、进程/文件双锁、目标版本必须等于最近验签缓存 |
| 数据库迁移失败或候选 App 不健康 | 维护窗口、SQLite Backup API、integrity check、哈希、旧 image/container snapshot 和自动回滚 |
| Updater 中止 | 每阶段原子持久化；启动恢复对已备份操作执行回滚，否则进入 `FailedNeedsOperator` |
| 恶意 Compose/Updater 自更新 | 在线路径只替换 App；Updater、Compose、权限或 contract 变化必须手工完整 bundle |
| Secret 泄漏 | token/public key 文件只读挂载；Authorization header 日志脱敏；状态和 Problem Details 不含 Secret |

## 4. Docker 权限约束

- Updater 不是 privileged，不使用 host network，不映射主机端口；
- 容器保持 non-root、`read_only`、`cap_drop: ALL` 和 `no-new-privileges`；
- 仅通过 Docker Socket 实际 GID 的 supplemental group 访问 Socket；
- App 容器永远不挂载 Socket；
- Updater 只挂载 Socket、app-data、updater-state、token 和发布公钥；
- Docker 操作使用 Docker.DotNet，不执行 shell `docker` 命令。

Docker Socket 风险不能被容器 capability 完全消除。最小 API、固定 allowlist、不可达性、签名验证和独立审查共同构成安全边界。

## 5. 数据一致性与回滚边界

1. App 拒绝新业务写入并暂停 Quartz；
2. 检查没有活动报告任务并 checkpoint WAL；
3. Updater 使用 SQLite Backup API 写入 updater-state；
4. 对备份执行 `integrity_check` 和 SHA-256；
5. 持久化旧容器契约后才停止并替换 App；
6. 候选 App 保持验证维护模式，不能接受业务写入；
7. 连续健康、版本、contract、operation ID 握手成功后才解除维护；
8. 任何备份后失败都恢复数据库、旧镜像标签和旧容器。

如果旧版本也无法恢复健康，停止自动破坏性操作并进入 `FailedNeedsOperator`，保留候选数据库、备份和操作状态。

## 6. 已知限制

- 不支持 rootless Docker、Podman、Swarm、Kubernetes 和 arm64；
- 主机磁盘、Docker daemon 或 volume 本身损坏可能需要人工恢复；
- 发布公钥轮换依赖手工完整 bundle；
- 首版不在线更新 Updater，不支持任意降级或后台自动安装；
- GitHub Release 可用性影响检查和下载，但不影响当前 App 运行。

## 7. 发布验收

开放 `InstallationEnabled=true` 前必须通过：

- token 缺失/错误、未知字段、未知 URL 和篡改签名拒绝测试；
- hash、大小、image ID、label、架构和 contract 不匹配测试；
- 下载中断、并发操作和 Updater 中止恢复测试；
- SQLite 备份、篡改备份、migration/health 失败和回滚测试；
- 干净 VM 上的 Socket 权限、签名候选安装和 App-only 更新测试；
- App 容器无 Socket、Updater 无主机端口及公开仓库 Secret 扫描。
