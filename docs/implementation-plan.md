# 实施计划

- 状态：执行基线
- 最近核对：2026-08-28
- 目标：交付公开、单管理员、Docker Compose 单实例的稳定报告系统，并支持签名 GitHub Release 安装和 App-only 安全在线升级。

## 1. 计划原则

本计划只保留实现产品目标和安全发布所必需的工作：

- 报告采集、快照、投递和计划任务必须形成可恢复的业务闭环；
- 普通用户必须能从 GitHub Release bundle 安装，不依赖公共容器 Registry；
- 发布物必须经过签名、哈希、架构和版本校验；
- 在线升级必须具备一致性备份、健康验证和自动回滚；
- 高权限 Updater 未通过安全验收前不得挂载 Docker Socket，也不得开放安装能力；
- 真实实现、自动测试和环境验收优先于计划文本中的完成声明。

贡献者社区建设、更多运行平台和便利性自动化不阻塞 1.0，统一列入“可延后项”。

## 2. 精简版本路线

```text
0.7.0  内部里程碑：报告业务闭环和持久化计划任务
0.8.0  内部里程碑：生产 Docker 部署和签名 bundle
0.9.0  内部里程碑：App-only 在线升级和自动回滚
1.0.0  首次公开 Release：安全验收、恢复演练和稳定版文档
```

`0.x` 只表示开发能力阶段，不创建 Git Tag 或 GitHub Release。M9 完成后直接准备并发布首个公开版本 `v1.0.0`，不再单独设置 `0.10.0`。

## 3. 当前真实进度

| 里程碑 | 对应版本 | 当前状态 | 真实结论 |
| --- | --- | --- | --- |
| M0 公开仓库基础 | `v1.0.0` 发布前置 | 已完成 | GitHub 仓库已公开；README、Apache-2.0、`SECURITY.md` 和 CI 已推送；Private Vulnerability Reporting、Secret Protection 和 Push protection 已启用，当前 0 个 Secret alert |
| M1 应用骨架 | 0.1.0 | 已完成 | .NET 模块、React SPA、SQLite、配置、健康检查和质量门已落地 |
| M2 初始化和认证 | 0.2.0 | 已完成 | 单管理员初始化、Cookie、CSRF、step-up 和主机恢复码已落地 |
| M3 Sub2API 同步 | 0.3.0 / 0.6.0 | 已完成 | 用户与 API Key 直接统计模型已落地，人员归属模型已移除 |
| M4 报告引擎 | 0.4.0 / 0.6.0 | 已完成 | 动态完整自然日窗口、schema v4 快照、CSV 和报告页面已落地 |
| M5 投递渠道 | 0.5.0 | 已完成 | 邮件、钉钉、飞书、限时下载和失败补发已落地 |
| M6 计划任务 | 0.7.0 | 已完成 | Quartz 持久化计划、窗口冻结、规范化执行记录、重试和恢复已落地 |
| M7 Docker 和签名 bundle | 0.8.0 内部里程碑 | 已完成 | Candidate workflow 在 linux/amd64 上通过签名、Critical 扫描、SBOM、安装、故障回滚、成功更新和 non-root Socket 权限验收 |
| M8 在线升级 | 0.9.0 内部里程碑 | 已完成 | 固定仓库验签、严格下载、Docker App-only 事务、维护模式、SQLite 备份、恢复、App API、step-up 和更新页面已实现 |
| M9 稳定版加固 | 首次公开 `v1.0.0` | 发布候选 | Updater 威胁模型、269 个 .NET 测试、20 个前端测试、Secret Protection 和候选恢复演练已完成；待最终 Release workflow |

当前自动质量门最近一次通过：

- .NET 格式检查和 Release 构建通过，`269` 个测试通过；
- 前端 typecheck、lint、build 通过，`20` 个测试通过；
- ShellCheck、Actionlint、Critical 镜像扫描、SBOM、changelog、签名、安装和回滚候选测试通过。

正式发布尚未完成：

- 项目版本和 changelog 已冻结为 `1.0.0`；
- 尚未创建首个 Tag `v1.0.0` 和 draft Release；
- 最终 Release Assets、attestation 和正式 bundle smoke test 待 Tag workflow 验收。

## 4. 已完成业务基线（M1-M6）

以下能力不再重复规划，只在回归失败时修复：

- .NET 10 模块化单体、React/TypeScript/Vite SPA 和 SQLite；
- 数据库动态配置、revision 并发控制和运行期刷新；
- 单管理员安全初始化、登录、密码修改、step-up 和恢复码；
- Sub2API 连接、Secret 加密、用户与 API Key 自动同步；
- 滚动 7/30 日、上一自然周/月和手工自定义自然日窗口；
- schema v4 不可变快照、历史读取兼容和 UTF-8 BOM CSV；
- 邮件、钉钉和飞书组合投递、逐渠道状态和失败补发；
- 限时 CSV 下载授权；
- Quartz 持久化月报计划、窗口冻结、幂等、任务级重试和重启恢复；
- 报告、计划、渠道、Key 和系统设置页面。

## 5. M7：完成生产部署和签名候选 bundle

### 5.1 `main` 已实现

- multi-stage App 和 Updater Dockerfile；
- production Compose 使用本地镜像标签并设置 `pull_policy: never`；
- 独立开发 Compose override 和 `dev-up.sh`；
- `docker save` 生成的 linux/amd64 App/Updater 压缩镜像归档；
- 签名 release manifest、SHA-256、镜像 ID、架构和版本校验；
- `install.sh` 首次安装；
- `update.sh` 手工部署契约更新、独立数据库备份和失败恢复；
- GitHub PR/Main 质量 workflow；
- GitHub Tag Release workflow、Critical 漏洞扫描、SBOM 和 artifact attestation；
- Apache-2.0 许可证进入源码、镜像、Release Assets 和完整 bundle；
- `CHANGELOG.md`、版本章节校验、Release 页面说明和随包 Release notes；
- Release workflow 中的 bundle 校验和安装 smoke test。

### 5.2 必须完成

1. 配置 `RELEASE_SIGNING_KEY_PEM`，保留离线备份并记录恢复方式。
2. 在隔离的 linux/amd64 环境运行发布构建脚本，生成签名候选 bundle；不创建 `0.x` Tag 或 GitHub Release。
3. 在干净 linux/amd64 VM 从候选 bundle 执行 `install.sh`。
4. 验证首次初始化、登录、容器重建和数据卷持久化。
5. 使用两个内部候选 bundle 执行 `update.sh`，验证配置、token、实例 ID 和数据保留。
6. 注入 migration 失败和 readiness 失败，验证旧镜像和升级前数据库恢复。
7. 验证候选 bundle 的 checksum、签名、镜像元数据、许可证和 changelog；SBOM 与 attestation 在最终 Release workflow 验收。
8. 提供并演练一套可执行的主机备份/恢复命令；不要求自动周备份。

### 5.3 M7 完成门

只有以下结果全部成立，M7 才能标记完成：

- 签名候选 bundle 构建和校验成功，且没有创建任何 `0.x` Tag 或 Release；
- 干净 amd64 VM 安装成功；
- 容器重建不丢数据；
- 手工部署契约更新成功；
- 至少一次失败更新完整恢复旧 App 和 SQLite；
- 镜像不包含源码、`.env`、数据库、报告、日志或测试结果；
- 仓库和候选 bundle 不包含真实身份、凭证或生产数据。

## 6. M8：完成 0.9.0 内部在线升级里程碑

### 6.1 最小范围

在线升级只替换 App。Updater、Compose、端口、卷、权限或部署契约变化继续要求管理员下载完整 bundle 并执行 `update.sh`。

必须实现：

1. App 更新检查、计划、安装和状态 API；安装操作要求管理员 step-up。
2. App 与 Updater 之间的固定 shared-token 认证和最小请求模型。
3. 固定 GitHub owner/repository、HTTPS Release 路径和 redirect allowlist。
4. manifest 签名、SemVer、linux/amd64、deployment contract、归档 SHA-256、大小、镜像 ID 和 Release notes 校验。
5. 持久化 upgrade lock、operation state 和中断恢复。
6. 报告任务、磁盘、Docker、数据卷、当前健康状态和版本兼容 preflight。
7. App 镜像归档流式下载、大小限制、超时、临时文件和原子完成。
8. 通过受限 Docker API 加载版本镜像，不允许任意 URL、命令、容器或 Docker 操作。
9. SQLite 一致性备份和校验。
10. 维护模式、停止新业务写入并等待活动任务结束。
11. 候选 App 替换、Migrator、连续健康阈值和版本/schema/contract 验证。
12. 失败时自动恢复旧 image ID 和升级前数据库。
13. 更新页面显示版本说明、preflight、阶段、错误摘要和重连状态。

### 6.2 安全完成门

- Updater 威胁模型和代码审查完成；
- App 容器始终没有 Docker Socket；
- Updater 没有主机端口，只管理当前 instance ID 的 App；
- 未知路由、字段、URL、镜像和 Docker 动作被拒绝；
- 下载中断、磁盘不足、备份失败、migration 失败、候选 App 退出、readiness 超时、Updater 中止和 Docker daemon 短时不可用均有故障注入测试；
- 失败更新可以自动恢复并留下脱敏审计记录；
- 上述门槛全部通过后，才能挂载 Docker Socket 并将 `InstallationEnabled` 改为 `true`。

## 7. M9：完成 1.0 稳定版

M9 只保留稳定发布必需项：

### 安全

- 高权限 Updater 独立审查无未解决的高危问题；
- 依赖和最终容器镜像没有未接受的 Critical 漏洞；
- Secret、token、webhook、报告内容和升级错误日志脱敏测试通过；
- Release 签名密钥备份、丢失恢复和轮换演练完成；
- 公开仓库敏感信息扫描通过。

### 恢复和运维

- 主机备份、恢复、升级失败和数据库损坏流程至少演练一次；
- 安装、初始化、配置、备份、升级、回滚和故障恢复文档可按步骤执行；
- 明确支持的 Docker Engine、Compose v2 和 linux/amd64 基线；
- 内部候选 bundle 到最终 `v1.0.0` 的安装和升级路径通过。

### 质量

- `pnpm quality` 通过；
- Release workflow 和干净 VM smoke test 通过；
- 核心报告、投递、调度和升级故障测试通过；
- 无阻塞发布的已知数据丢失、认证绕过或升级失效问题。

完成后先创建 `1.0.0` draft Release，人工审核 Assets、changelog 和恢复证据，再发布正式版本。

## 8. 1.0 非阻塞项

以下项目有价值，但不影响当前单管理员 Compose 产品达到 1.0：

- `CONTRIBUTING.md`、Code of Conduct、Issue/PR 模板和 CODEOWNERS；
- Dependabot/Renovate、CodeQL 和复杂 branch protection；
- 自动周备份、远端备份上传和备份管理页面；
- Updater 在线自更新；
- 自动后台安装、任意历史版本降级和多发布通道；
- 公共容器 Registry；
- arm64、rootless Docker、Podman、Swarm 和 Kubernetes；
- 多管理员、多租户、实时用量监控和请求内容审计；
- 全站自动 axe 门、完整键盘审计和专门的 100 Key 性能基准；
- 多语言 UI。

这些项目可以在 1.0 后按实际用户需求进入独立里程碑。基础可访问性、无水平溢出和普通规模性能问题仍按缺陷处理。

## 9. 最小 CI 门

| 检查 | PR/Main | Release |
| --- | ---: | ---: |
| .NET format/build/test | 必须 | 必须 |
| frontend typecheck/lint/test/build | 必须 | 必须 |
| Bash、Compose 和 changelog 校验 | 必须 | 必须 |
| Secret/私钥扫描 | 必须 | 必须 |
| linux/amd64 容器构建 | Main 可延后 | 必须 |
| Critical 容器漏洞扫描 | 否 | 必须 |
| SBOM、签名和 attestation | 否 | 必须 |
| bundle 解包、签名验证和安装 smoke test | 否 | 必须 |
| 在线升级故障注入 | M8 后必须 | 必须 |

不再要求 nightly Job 作为 1.0 前置；关键故障测试直接进入 PR 或 Release 门。

## 10. Definition of Done

功能或里程碑只有在以下条件满足后才能标记完成：

- 行为和失败语义已经实现，不是 scaffold 或只写文档；
- 核心规则有自动测试；
- API、前端、部署脚本和文档保持一致；
- 日志、审计、测试和发布物不包含敏感数据；
- `pnpm quality` 通过；
- 涉及 Docker 或升级时，真实容器和失败恢复验收通过；
- 对应 changelog 已更新；
- 不存在会导致数据丢失、认证绕过或不可恢复升级失败的已知问题。

## 11. 下一执行顺序

1. 提交并推送 `1.0.0` 版本、正式 README、changelog 和最终文档。
2. 创建并推送仓库首个 Tag `v1.0.0`。
3. 等待 Release workflow 完成质量、Critical 扫描、SBOM、签名、attestation 和 bundle smoke test。
4. 下载并独立校验 draft Assets、manifest 签名、checksums 和安装包内容。
5. 发布 draft Release，并确认公开下载地址、README 安装命令和安全报告入口可用。
