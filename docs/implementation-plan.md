# 实施计划

- 状态：设计基线
- 原则：先完成可验证的报告闭环，再交付高权限在线升级能力。

## 1. 版本路线

```text
0.1.0  repository foundation + application shell
0.2.0  secure bootstrap + administrator session
0.3.0  Sub2API connection + people/key mapping
0.4.0  report collection + snapshot + CSV
0.5.0  email/dingtalk/feishu delivery
0.6.0  persistent scheduling + run history
0.7.0  production Docker deployment + release pipeline
0.8.0  online update + rollback
0.9.0  security hardening + release candidate
1.0.0  first stable release
```

版本号表示能力成熟度，不要求每个中间版本都面向普通用户发布镜像。

## 2. 阶段依赖

```text
M0 Repository
  -> M1 Application shell
  -> M2 Setup and identity
  -> M3 Sub2API and key ownership
  -> M4 Report engine
  -> M5 Delivery channels
  -> M6 Scheduling
  -> M7 Docker and releases
  -> M8 Online updater
  -> M9 Release hardening
```

M5 的三个渠道可以并行实现，但必须共用同一 Sender contract 和 delivery state machine。

## 3. M0：公开仓库基础

### 交付

- GitHub public repository；
- `README.md`、`LICENSE`、`SECURITY.md`、`CONTRIBUTING.md`、Code of Conduct；
- `.editorconfig`、`.gitattributes`、`.gitignore`；
- .NET solution 和 Node workspace 基础文件；
- Dependabot/Renovate 依赖更新策略；
- CodeQL、secret scanning、Gitleaks；
- Issue/PR templates，明确禁止上传生产数据；
- branch protection 和 required checks。

### 必须先决定

- 项目正式名称和 GitHub/GHCR namespace；
- 开源许可证；
- 安全漏洞私下报告渠道；
- Release 签名密钥保管方式。

### 验收

- 提交包含假 API Key、测试私钥或真实邮箱模式时 CI 能阻止；
- Docker build context 不包含 `.git`、`.env`、数据库、报告和日志；
- 所有测试 fixture 都是合成数据；
- PR 无法绕过 required checks 合并到默认分支。

## 4. M1：应用骨架

### 后端

- 创建 Domain/Application/Infrastructure/Api/Migrator 项目；
- 依赖方向测试；
- SQLite DbContext 和第一版 migration；
- 数据库优先的 typed settings service、revision 并发控制和运行期日志级别刷新；
- Problem Details、correlation ID、Serilog；
- `/health/live`、`/health/ready`；
- OpenAPI；
- React SPA fallback 和静态资源缓存规则。

### 前端

- React + TypeScript + Vite；
- shadcn/ui、Tailwind、Lucide；
- React Router 和 TanStack Query；
- 工作台壳、侧栏、页面标题、错误边界；
- light/dark/system theme；
- 中文默认文案；
- Playwright 和 axe 基线。

### 构建

- 前端 build 输出复制到 ASP.NET Core `wwwroot`；
- 本地开发由 Vite proxy `/api` 到 ASP.NET Core；
- Release 构建只产生一个 App Web artifact。

### 验收

- `dotnet test`、frontend lint/test/build 全部通过；
- App 同源提供 API 和 SPA；
- 刷新任意前端路由不会 404；
- API 路由不会被 SPA fallback 吞掉；
- 修改数据库设置后，读取方和日志级别无需重启即可使用新 revision；
- 桌面和移动视口无水平溢出。

## 5. M2：首次初始化和认证

- 状态：已实现（0.2.0）

### 交付

- Setup challenge 生成、哈希、过期和失败限流；
- Docker 日志一次性初始化码；
- 初始化状态 API；
- 管理员创建事务和唯一管理员约束；
- ASP.NET Core Identity Cookie 登录；
- antiforgery header；
- 登录、登出、修改密码；
- step-up 授权；
- 主机侧密码恢复码 CLI；
- 初始化页、登录页和安全设置页；
- 受认证的系统设置查询/更新 API 和页面，不暴露业务配置环境变量；
- 配置更新 revision 冲突响应和审计记录。

### 安全测试

- 两个并发初始化请求只能成功一个；
- 初始化完成后旧 code 和 setup endpoint 均不可用；
- 重启前后 setup code 行为符合设计；
- Cookie flag、session rotation、CSRF 和 rate limiting；
- 日志不出现密码、Cookie 或 code hash。

### 验收

全新数据卷可以完成初始化，重建容器后管理员仍能登录；删除 App 容器不会重新开放初始化。

## 6. M3：Sub2API 和人员-Key 映射

- 状态：已实现（0.3.0）

### 交付

- 单 Sub2API connection 配置；
- Admin API Key 加密存储和掩码更新；
- connection test；
- 用户 ID、Codex group ID 配置；
- API Key 分页同步；
- Key 名称、状态和最后使用时间 snapshot；
- 人员 CRUD；
- 一人多 Key 和 Key 轮换有效期；
- 未映射、重复映射和已退休 Key 检查；
- 人员与 Key 管理页面。

### Stub 场景

- 正常分页；
- 空数据；
- 401/403；
- 404 表示部署版本不兼容；
- 429 + Retry-After；
- 500/超时；
- Key 被重命名、停用、删除；
- Sub2API 返回未知字段。

### 验收

管理员可以连接测试实例、同步 Key、完成全部归属，并清楚看到任何未映射 Key。数据库和日志不保存完整业务 Key。

## 7. M4：报告引擎

- 状态：已实现（0.4.0）

### 交付

- 7/30 个完整自然日窗口；
- `Asia/Shanghai` 及可配置 IANA 时区；
- 按 Key 调用 Sub2API stats；
- bounded concurrency、timeout、retry；
- Key -> 人员聚合；
- 全员总计；
- immutable canonical snapshot；
- UTF-8 BOM CSV；
- 手工 dry-run，不发送渠道；
- 报告列表和详情页。

### 金额和数值

- 数据库存储费用使用 decimal，不使用 binary float 做二次计算；
- 保留 Sub2API 原始精度；
- 展示层统一格式化；
- Token 和请求数使用 64-bit integer；
- snapshot 明确 schema version 和统计时区。

### 验收

固定 stub 数据生成的 JSON/CSV golden files 稳定；跨月、闰年和一人多 Key 汇总正确；部分 Key 失败时报告不会伪装成完整成功。

## 8. M5：发送渠道

### 公共能力

- `IReportSender` contract；
- channel config 加密、掩码和 test send；
- payload hash；
- 每渠道独立状态和重试；
- HTTP 200 + 业务错误码检查；
- 消息长度预算和分片；
- 渠道组合编排。

### Email

- SMTP TLS/STARTTLS；
- HTML summary；
- CSV attachment；
- To/CC；
- header injection 防护；
- synthetic preview recipient。

### DingTalk

- HMAC-SHA256 签名；
- Markdown 子集；
- 限流响应；
- webhook host 和 redirect 控制。

### Feishu

- HMAC-SHA256 签名；
- `post` 富文本；
- payload size 和频率控制；
- webhook host 和 redirect 控制。

### 验收

任意渠道失败不阻断其他渠道；补发只触发失败渠道；测试、日志和截图不包含真实 webhook 或收件人。

## 9. M6：计划任务和运行历史

### 交付

- Quartz persistent JobStore；
- 每月日期、时间和时区设置；
- 默认每月 1 日 09:00 `Asia/Shanghai`；
- disallow concurrent execution；
- misfire policy；
- scheduled idempotency key；
- 手工运行和失败渠道补发；
- 下次运行时间；
- ReportRun/Delivery 状态和审计；
- 进程重启恢复。

### 验收

- 重复触发只产生一份计划报告；
- 执行中重启后不会静默重复发送；
- 修改时区后下次运行时间正确；
- 页面能区分成功、部分失败、采集失败和发送失败。

## 10. M7：Docker 和发布

### 交付

- multi-stage App Dockerfile；
- amd64 Updater 镜像基础；
- production Compose；
- named volumes、healthcheck、security options；
- install bundle 和 checksum；
- GitHub Actions build/test/scan；
- GHCR publish；
- SBOM 和 artifact attestation；
- signed release manifest；
- weekly/manual backup；
- 安装、升级前检查和恢复文档。

### 验收

干净 amd64 Linux VM 从 Release bundle 部署成功；容器重建不丢数据；镜像不包含源码、Node cache、测试结果、`.env` 或开发证书。

## 11. M8：在线升级

按 [online-update.md](online-update.md) 实施。

### 交付

- Controller internal API 和 shared token；
- manifest 签名、digest、仓库和 contract 验证；
- ephemeral worker；
- upgrade lock 和 durable state；
- preflight；
- SQLite 一致性备份；
- maintenance mode；
- App replace、migration、health threshold；
- Controller handoff；
- automatic rollback；
- update page 和 reconnect polling。

### 发布门槛

Updater 上线前必须完成威胁建模、代码审计和故障注入测试。若门槛未满足，0.8 版本只开放“检查更新”，不开放安装按钮。

## 12. M9：稳定版加固

### 安全

- CSP、frame ancestors、nosniff、referrer/permissions policy；
- Fetch Metadata；
- dependency and container vulnerability review；
- secret redaction tests；
- backup/restore drill；
- release signing drill；
- updater penetration review。

### 质量

- 关键页面 keyboard-only 验证；
- Playwright desktop/mobile screenshots；
- axe 扫描；
- 100 Key 性能场景；
- 长消息分片；
- SQLite 磁盘满和锁冲突；
- Sub2API 长时间不可用；
- SMTP/webhook 限流。

### 文档

- 安装、初始化、配置、报告、备份、升级、恢复；
- 安全报告流程；
- 数据保留和隐私说明；
- Release notes 和迁移说明。

## 13. CI 检查矩阵

| 检查 | PR | Main | Release |
| --- | ---: | ---: | ---: |
| .NET format/build/test | yes | yes | yes |
| frontend lint/typecheck/test/build | yes | yes | yes |
| architecture dependency tests | yes | yes | yes |
| API integration tests | yes | yes | yes |
| Playwright smoke | yes | yes | yes |
| full E2E | optional | yes | yes |
| secret/privacy scan | yes | yes | yes |
| CodeQL | yes | yes | yes |
| dependency review | yes | yes | yes |
| container build | optional | yes | yes |
| container vulnerability scan | no | yes | yes |
| SBOM/attestation/signing | no | no | yes |
| update/rollback fault tests | no | nightly | yes |

## 14. Definition of Done

功能只有在以下条件全部满足后才能完成：

- 行为和失败语义已实现；
- 单元/集成测试覆盖核心规则；
- 关键用户流程有 E2E；
- API 契约和前端类型同步；
- 日志和审计脱敏；
- 文档更新；
- 无已知高危依赖漏洞；
- 无真实身份、内部信息、凭证或生产数据；
- Docker 构建和健康检查通过；
- 相关迁移经过空库和前一稳定版数据库验证。

## 15. 开发开始前仍需决策

- 项目正式名称；
- GitHub owner 和 GHCR namespace；
- 开源许可证；
- 默认 UI 是否只提供中文；
- 安全漏洞接收邮箱或 GitHub Security Advisories 流程；
- Release manifest 签名密钥托管方式；
- 正式支持的 Docker Engine 最低版本；
- 首版是否包含 TOTP，或放到 1.x。
