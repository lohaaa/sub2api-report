# Sub2API Report 系统架构与技术方案

- 状态：设计基线
- 日期：2026-08-26

## 1. 产品定位

Sub2API Report 是一个单管理员、单实例的内部运营工具，用于：

- 按 Sub2API 用户展示账号下的 API Key；
- 统计每个 API Key 最近 7 天和 30 天的 Codex 用量，并按用户小计；
- 每次生成报告前自动刷新 Sub2API 用户与 Key，刷新失败则终止并记录错误；
- 通过邮箱、钉钉、飞书任意组合发送；
- 留存报告、发送结果和操作审计；
- 在管理页面检查并一键升级系统。

系统不是多租户 SaaS，不承担 Sub2API 的代理流量，也不保存 API 请求内容。

## 2. 已确认的架构决策

| 决策 | 选择 | 原因 |
| --- | --- | --- |
| 后端 | .NET 10 / ASP.NET Core | 用户指定；适合一体化 Web、后台任务和容器部署 |
| 前端 | React + TypeScript + Vite + shadcn/ui | shadcn/ui 的标准 React 路线，构建后可由 ASP.NET Core 同源托管 |
| 应用形态 | 模块化单体 | 单管理员、单实例，无需微服务复杂度 |
| 主数据库 | SQLite | 零外部依赖，适合 Docker 一键部署和当前数据规模 |
| 调度 | 应用内 Quartz.NET 持久化调度 | 可在页面配置、查看执行记录和手工补跑 |
| 认证 | ASP.NET Core Identity + Cookie | 前后端同源，避免在浏览器保存 JWT |
| 首次初始化 | Docker 日志一次性初始化码 | 防止公网首访者抢注管理员 |
| 部署 | Docker Compose | 一条命令启动主应用和内部 updater |
| 在线升级 | 页面触发 updater sidecar | 主应用不接触 Docker Socket，可健康检查和回滚 |
| 配置管理 | SQLite typed settings + 运行期刷新 | 可变配置通过页面修改并动态生效，部署配置只负责启动闭环 |
| 镜像架构 | linux/amd64 | 用户确认当前只需要 amd64 |
| 发布 | GitHub Release + GHCR | 代码、发行说明、镜像和校验信息统一管理 |

## 3. 总体架构

```text
Browser
  |
  | HTTPS / same-origin cookie
  v
+------------------------------------------------------+
| sub2api-report-app                                   |
|                                                      |
|  ASP.NET Core 10                                     |
|  +----------------+  +----------------------------+  |
|  | REST API       |  | React/Vite/shadcn SPA      |  |
|  +----------------+  +----------------------------+  |
|  | Identity       |  | Setup / Admin / Audit      |  |
|  | Report Engine  |  | Quartz Scheduler           |  |
|  | Sub2API Client |  | Email/DingTalk/Feishu      |  |
|  | Update Client  |  | Static file host           |  |
|  +------------------------------------------------+  |
|             | SQLite + files on /data               |
+-------------+----------------------------------------+
              |
              +---- HTTPS ----> Sub2API
              +---- SMTP  ----> Mail server
              +---- HTTPS ----> DingTalk / Feishu
              |
              | private Docker network + shared token
              v
+------------------------------------------------------+
| sub2api-report-updater                               |
|  fixed operation allowlist                          |
|  release verification / pull / replace / rollback   |
|  only component with Docker Engine socket           |
+--------------------------+---------------------------+
                           |
                           v
                    Docker Engine / GHCR
```

只有主应用暴露 Web 端口。Updater 不映射主机端口，也不提供通用 Docker 代理能力。

## 4. 代码仓库结构

采用 monorepo：

```text
/
├─ src/
│  ├─ Sub2ApiReport.Api/             # HTTP API、认证、SPA 托管、DI
│  ├─ Sub2ApiReport.Application/     # 用例、命令查询、端口接口
│  ├─ Sub2ApiReport.Domain/          # 实体、值对象、领域规则
│  ├─ Sub2ApiReport.Infrastructure/  # EF Core、外部客户端、渠道实现
│  ├─ Sub2ApiReport.Migrator/        # 启动和升级时执行数据库迁移
│  ├─ Sub2ApiReport.UpdateContracts/ # App 与 Updater 的最小协议
│  └─ Sub2ApiReport.Updater/         # 独立升级控制器
├─ web/
│  ├─ src/
│  │  ├─ app/                        # Router、QueryClient、全局 Provider
│  │  ├─ features/                   # 按业务功能组织页面和组件
│  │  ├─ components/ui/              # shadcn/ui 组件
│  │  ├─ components/layout/          # 应用壳、导航、页面布局
│  │  ├─ lib/                        # API client、格式化、校验
│  │  └─ styles/
│  ├─ tests/
│  └─ vite.config.ts
├─ tests/
│  ├─ Sub2ApiReport.UnitTests/
│  ├─ Sub2ApiReport.IntegrationTests/
│  ├─ Sub2ApiReport.ArchitectureTests/
├─ deploy/
│  ├─ compose.yaml
│  ├─ .env.example
│  ├─ install.sh
│  └─ upgrade-contract.json
├─ docs/
├─ .github/workflows/
├─ Directory.Build.props
├─ Directory.Packages.props
├─ Sub2ApiReport.slnx
├─ Dockerfile
└─ Dockerfile.updater
```

依赖方向：

```text
Domain <- Application <- Api
   ^          ^           |
   +----------+-----------+
       Infrastructure

UpdateContracts <- Api
UpdateContracts <- Updater
```

Updater 不引用业务 Infrastructure，不读取业务实体，也不能调用任意 Docker 操作。

## 5. 后端设计

### 5.1 模块边界

采用模块化单体，按业务能力分区：

| 模块 | 职责 |
| --- | --- |
| Setup | 初始化状态、一次性初始化码、首个管理员创建 |
| Identity | 登录、登出、修改密码、会话和安全审计 |
| Sub2Api | 连接配置、连通性检查、用户/Key 同步、用量查询 |
| Reports | 日期窗口、自动刷新、采集、聚合、快照、CSV/HTML 渲染 |
| Scheduling | 月报计划、Quartz Trigger、手工运行、补跑 |
| Notifications | 邮件、钉钉、飞书配置、测试和投递 |
| Updates | 版本检查、升级授权、状态查询、历史记录 |
| Audit | 管理操作、安全事件和关键配置变更 |
| System | 健康检查、版本、备份、运行状态 |

Controller/Endpoint 只负责协议转换、认证授权和输入校验。统计规则、幂等和发送编排位于 Application 层。

### 5.2 推荐后端组件

- ASP.NET Core 10 Minimal APIs 或 Controllers；同一项目内统一一种风格。
- ASP.NET Core Identity，仅允许一个管理员用户。
- EF Core 10 + SQLite provider。
- Quartz.NET，使用 SQLite 持久化 JobStore。
- `IHttpClientFactory` + Microsoft HTTP resilience handlers。
- MailKit/MimeKit 发送 SMTP HTML 邮件和 CSV 附件。
- CsvHelper 生成 UTF-8 BOM CSV。
- Serilog 输出结构化 JSON 日志到 stdout。
- OpenAPI 生成契约；TypeScript client 在构建阶段生成。

不引入消息队列、Redis、独立缓存和通用工作流引擎。

### 5.3 报表执行流程

```text
manual dry-run
  -> load connection, system settings, Key inventory and assignment snapshots
  -> calculate previous complete 7/30-day windows
  -> split each Key at the 7-day and assignment boundaries
  -> fetch per-key segment stats with bounded concurrency
  -> aggregate segment -> Key -> person -> totals
  -> mark failed, conflicting or materially unassigned segments
  -> freeze canonical report snapshot
  -> render UTF-8 BOM CSV from the stored snapshot
```

采集并发默认 4，可在 SQLite 中配置为 1 到 10，每次报告开始时固定该配置快照。单次请求超时 15 秒；网络错误和 `5xx` 最多尝试 3 次，`429` 尊重并限制 `Retry-After`，业务 `4xx` 直接失败。M6 在同一个报告引擎外增加 Quartz 触发、运行状态和渠道投递，不改变 canonical snapshot。

### 5.4 幂等与状态机

计划任务的幂等键：

```text
scheduled:{scheduleId}:{periodEnd:yyyy-MM-dd}
```

手工运行使用独立 ID，不覆盖计划报告，但可选择“仅重试失败渠道”。

`ReportRun` 状态：

```text
Queued -> Collecting -> Rendering -> Delivering -> Succeeded
                                              -> PartialFailed
          \-> Failed
```

0.5.0 的手工投递只使用该状态机的尾部：运行从 `Running` 进入，全部渠道成功
为 `Succeeded`，存在失败渠道为 `PartialFailed`，全部失败为 `Failed`。`Queued`、
`Collecting`、`Rendering` 和幂等键唯一索引由 M6 的计划运行启用，不重建状态机。

`Delivery` 状态：

```text
Pending -> Sending -> Succeeded
                   -> Failed
```

数据库对计划任务幂等键建立唯一索引。Quartz 的重复触发和进程重启不能产生重复月报。

### 5.5 时间口径

- 默认时区：`Asia/Shanghai`。
- 默认发送时间：每月 1 日 09:00。
- 报告不包含运行当天。
- 7 天和 30 天均使用完整自然日。
- 内部使用 UTC `Instant` 保存时间，业务窗口使用时区和 `DateOnly` 计算。
- 展示和 API 参数统一带明确时区，禁止依赖容器本地时区推断。

### 5.6 配置管理

遵循 [配置管理策略](configuration.md)：时区、发布通道、日志级别、数据保留、外部连接、通知渠道、计划任务和升级策略均写入 SQLite，并通过受认证管理 API 在运行期更新。业务模块在操作开始时读取 typed settings snapshot，不直接从 `IConfiguration` 读取业务配置。

数据库连接字符串、监听地址、运行环境、Updater 内部 token 文件和外部主密钥入口属于启动闭环例外。新增部署配置必须说明为什么无法在数据库加载后动态管理。

配置更新使用 revision 乐观并发并写审计；秘密字段加密存储。进行中的报告使用启动时快照，后续任务使用新 revision，避免同一运行中途改变统计和投递语义。

## 6. 数据设计

### 6.1 SQLite 配置

- 数据文件：`/data/db/sub2api-report.db`。
- 开启 WAL、foreign keys 和 busy timeout。
- 单应用实例写入；Updater 只在维护模式和应用停止后恢复备份。
- EF Core migration 由独立 Migrator 执行，主 Web 进程不隐式迁移。
- 升级前使用 SQLite backup API 生成一致性备份，不直接复制活动中的数据库文件。

### 6.2 核心表

| 表 | 关键字段 | 说明 |
| --- | --- | --- |
| `AdminUsers` + Identity tables | `Id`, `UserName`, `PasswordHash` | 唯一管理员；数据库约束只允许一个活动管理员 |
| `SystemSettings` | `InitializedAt`, `Timezone`, `ReleaseChannel`, `LogLevel`, retention fields, `Revision` | 可动态更新的单例系统设置 |
| `SetupChallenges` | `CodeHash`, `ExpiresAt`, `ConsumedAt` | 只保存初始化码哈希 |
| `Sub2ApiConnections` | `BaseUrl`, `AdminKeyCiphertext`, `LegacyUserId`, `UserScopeMode`, `CodexGroupId` | 当前只允许一个活动连接 |
| `Sub2ApiUsers` | `ExternalId`, `EmailSnapshot`, `Status`, `IsSelected` | 同步的上游用户快照与报告范围 |
| `ExternalApiKeys` | `ExternalId`, `Sub2ApiUserId`, `NameSnapshot`, `Status`, `GroupId`, `RetiredAt` | Sub2API Key 本地缓存，稳定标识为 `user_id + api_key_id`；报告生成前自动刷新 |
| `ReportGenerationRuns` | `Trigger`, `Status`, `Stage`, `ErrorCode`, `ErrorMessage` | 每次报告生成尝试，包含自动刷新失败信息 |
| `ReportSnapshots` | `Id`, `SchemaVersion`, `CutoffDate`, `Status`, `CanonicalJson`, cost summaries | M4 不可变报告快照；列表字段单独索引，明细只保留一份 canonical JSON |
| `ReportSchedules` | `DayOfMonth`, `LocalTime`, `Timezone`, `Enabled` | M6 计划任务，MVP 只有一条月报计划 |
| `NotificationChannels` | `Type`, `Name`, `Enabled`, `ConfigCiphertext` | M5 邮件、钉钉、飞书实例 |
| `ReportRuns` | `Id`, `SnapshotId`, `Trigger`, `Status`, `IdempotencyKey` | M5 手工投递运行（Trigger=ManualDelivery）；M6 增加计划触发并启用幂等键 |
| `DeliveryRecords` | `RunId`, `ChannelId`, `PayloadHash`, `Status`, `Attempts` | M5 手工投递逐渠道状态；M6 计划投递复用同一状态机 |
| `DeliveryParts` | `DeliveryId`, `PartIndex`, `PayloadHash`, `Status`, `Attempts` | M5 分片消息逐片状态，补发只重试失败分片 |
| `UpdateRecords` | `FromVersion`, `ToVersion`, `Status`, timestamps | 升级历史 |
| `AuditEvents` | `Actor`, `Action`, `Target`, `Result`, `MetadataJson` | 不保存密钥和密码 |

关键约束：

- 一个 Key 的用量始终使用其所属 Sub2API 用户的 `user_id` 查询；
- `ExternalApiKeys.ExternalId` 在同一用户下唯一；
- 0.6.0 起 `People`、`PersonApiKeyAssignments` 表已删除，历史报告快照保持不可变；
- 计划报告 `IdempotencyKey` 唯一；
- `DeliveryRecords(RunId, ChannelId)` 唯一；
- 报表快照生成后不可修改，只能生成新的补跑记录。

### 6.3 机密数据

以下字段加密后写入 SQLite：

- Sub2API Admin API Key；
- SMTP 密码；
- 钉钉 webhook/secret；
- 飞书 webhook/secret。

使用 ASP.NET Core Data Protection，key ring 持久化到 `/data/keys`。日志、审计和 API 响应永不返回完整秘密，只显示类型和末尾掩码。

应明确：若攻击者同时取得整个 `/data` 卷，默认一键部署模式下仍可能获得数据库和解密 key ring。高安全部署可通过 `APP_MASTER_KEY_FILE` 使用宿主机 Secret 或外部密钥管理系统保护 key ring。

## 7. 身份认证与首次初始化

### 7.1 初始化流程

1. Migrator 创建数据库。
2. 应用发现不存在管理员。
3. 生成高熵一次性初始化码，只保存哈希，并打印到 Docker 日志。
4. 未初始化时，前端只允许访问初始化页和 setup 状态 API。
5. 管理员输入初始化码、用户名和密码。
6. 服务端限流、校验、事务内创建唯一管理员并消费初始化码。
7. 初始化完成后，所有 setup 写接口永久返回 `404/409`，跳转登录页。

初始化码规则：

- 至少 128 bit 随机熵；
- 默认 30 分钟有效；
- 未初始化状态下重启会生成新码并使旧码失效；
- 最多 5 次失败尝试，随后短时锁定；
- 不通过 URL、环境变量回显或前端日志传递。

日志示例仅在未初始化时出现：

```text
Admin setup required. One-time setup code: XXXX-XXXX-XXXX-XXXX
Open: http://<host>:8080/setup
```

### 7.2 日常登录

- Cookie 名称使用 `__Host-` 前缀（HTTPS 部署）。
- `HttpOnly`、`Secure`、`SameSite=Lax`、`Path=/`。
- 滑动过期 8 小时，绝对上限 24 小时。
- 登录、修改密码和升级操作分别限流。
- 所有状态变更 API 使用 antiforgery token header。
- 登录成功后轮换会话；登出清除 Cookie 和站点认证数据。
- .NET 10 API 对未认证请求返回 `401`，前端统一跳转登录页。

在线升级、修改 Sub2API 密钥、恢复备份等高风险操作要求重新输入当前密码，产生短时 step-up 授权。

### 7.3 管理员恢复

提供主机侧恢复命令，不开放匿名“忘记密码”邮件流程：

```bash
docker compose exec app appctl admin create-reset-code
```

命令生成短时一次性恢复码并只写入当前终端，管理员在恢复页面设置新密码。该操作写入审计日志。

## 8. API 设计

统一前缀 `/api/v1`，使用 Problem Details 表达错误，所有写操作支持 correlation ID。

主要端点：

```text
GET  /api/v1/security/antiforgery
GET  /api/v1/setup/status
POST /api/v1/setup/initialize

POST /api/v1/auth/login
POST /api/v1/auth/logout
GET  /api/v1/auth/me
POST /api/v1/auth/change-password
POST /api/v1/auth/step-up
POST /api/v1/auth/recover

GET  /api/v1/sub2api/connection
PUT  /api/v1/sub2api/connection
POST /api/v1/sub2api/connection/test
GET  /api/v1/sub2api/keys
POST /api/v1/sub2api/keys/sync

GET/POST /api/v1/people
GET/PUT/DELETE /api/v1/people/{id}
GET/PUT/DELETE /api/v1/people/assignments/{id}
POST /api/v1/people/{id}/assignments

GET/PUT /api/v1/schedule

GET/POST/PUT/DELETE /api/v1/channels
POST /api/v1/channels/{id}/test

GET  /api/v1/reports
GET  /api/v1/reports/{id}
GET  /api/v1/reports/{id}/csv
POST /api/v1/reports/dry-run

GET/POST/PUT/DELETE /api/v1/channels
POST /api/v1/channels/{id}/test

GET  /api/v1/reports/{id}/deliveries
POST /api/v1/reports/{id}/deliveries
POST /api/v1/reports/{id}/deliveries/{runId}/retry

GET  /api/v1/system/version
GET  /api/v1/system/settings
PUT  /api/v1/system/settings
GET  /api/v1/updates/check
POST /api/v1/updates/install
GET  /api/v1/updates/status

GET  /health/live
GET  /health/ready
```

秘密配置的读取接口只返回 `configured: true` 和掩码。更新时空值表示保持原秘密，必须显式执行“清除”。

## 9. 前端架构与体验

### 9.1 技术栈

- React + TypeScript + Vite。
- shadcn/ui + Tailwind CSS。
- React Router。
- TanStack Query 管理服务端状态。
- React Hook Form + Zod 管理表单和前端校验。
- OpenAPI 生成 TypeScript client，减少前后端 DTO 漂移。
- Lucide React 图标。
- Recharts 仅用于确实需要的趋势图；所有图表提供数据表替代。
- Vitest + Testing Library + MSW。

### 9.2 页面信息架构

初始化完成后首屏直接进入工作台，不制作营销 Landing Page：

```text
工作台
API Keys
报告记录
发送渠道
计划任务
系统设置
  ├─ Sub2API
  ├─ 安全
  ├─ 备份
  └─ 在线升级
审计日志
```

工作台优先展示：

- 下次计划运行时间；
- 最近一次报告状态；
- 30 天总费用和总 Token；
- 未映射 Key 数量；
- 失败渠道和可重试操作；
- 当前版本和可用更新。

界面采用安静、紧凑的运营工具风格：固定侧栏、清晰页面标题、数据表和行内操作。页面区块保持无框或全宽，不把每个区块都做成浮动卡片，也不嵌套卡片。

### 9.3 初始化向导

初始化不是普通注册页：

1. 输入 Docker 日志初始化码；
2. 创建管理员用户名和密码；
3. 登录后进入配置清单：连接 Sub2API、同步用户并选择范围、配置渠道、确认计划。

步骤使用有语义的进度导航，表单有显式 label、错误摘要和焦点管理。初始化码使用 `autocomplete="one-time-code"`，用户名和密码使用正确 autocomplete 属性。

### 9.4 可访问性和浏览器安全

- 使用语义化 landmark、标题层级和数据表 caption/header。
- 所有图标按钮有 tooltip 和可访问名称。
- 键盘焦点清晰，禁止正 tabindex。
- Dialog 使用 shadcn/Radix 已验证的焦点管理，不自行编写 focus trap。
- 状态不能只靠颜色表达，配合图标和文字。
- 支持 200% 缩放、窄屏和 `prefers-reduced-motion`。
- SPA 每次路由切换更新 document title，并将焦点移到主标题。
- 不使用 `dangerouslySetInnerHTML` 渲染外部错误或报告内容。
- 静态资源全部本地打包，不依赖运行时 CDN。

## 10. 安全基线

- 生产环境必须置于 HTTPS 反向代理后。
- 默认拒绝跨域，不启用 CORS；前后端同源。
- Cookie 认证 + antiforgery header。
- CSP 从首版直接执行，Vite 不允许内联脚本；根据组件实际行为收紧 style policy。
- `frame-ancestors 'none'` / `X-Frame-Options: DENY`。
- `X-Content-Type-Options: nosniff`。
- `Referrer-Policy: strict-origin-when-cross-origin`。
- `Permissions-Policy` 禁用 camera、microphone、geolocation 等未使用能力。
- Fetch Metadata 拒绝跨站非导航 API 请求。
- 所有 URL 配置禁止凭证出现在 query 日志中，HttpClient 禁止把 secret 写入诊断日志。
- Sub2API 可配置内网地址；钉钉、飞书 webhook 默认校验官方 HTTPS host，显式高级开关才允许代理地址。
- 主应用容器不挂载 Docker Socket。
- Updater 的 Docker Socket 权限视为宿主机 root 权限，按独立高风险组件审计。

## 11. 可观测性与运维

日志输出到 stdout，由 Docker 收集：

- 启动、版本、数据库迁移；
- 计划任务触发和完成；
- Sub2API 请求耗时、状态码、重试次数，不记录 Key；
- 每个渠道的投递结果，不记录 webhook；
- 登录失败、初始化、step-up、升级和恢复操作；
- correlation ID、report run ID、delivery ID。

健康检查：

- `/health/live`：进程事件循环可响应。
- `/health/ready`：数据库可用、迁移完成、非升级维护状态。
- 外部 Sub2API/SMTP/webhook 不纳入 readiness，避免第三方故障导致容器重启风暴。

MVP 不强制引入 Prometheus；保留 OpenTelemetry 接入点。

## 12. 测试策略

### 单元测试

- 7/30 天日期窗口和跨月、闰年、时区边界；
- 一人多 Key 聚合；
- Key 轮换有效期；
- 费用精度和 CSV 格式；
- 渠道签名；
- 幂等键和状态机；
- Release 版本比较和升级策略。

### 集成测试

- SQLite migration 和唯一约束；
- Identity 初始化竞态，只能创建一个管理员；
- Cookie + antiforgery；
- Sub2API stub 的分页、限流、错误响应；
- SMTP 和 webhook mock server；
- Quartz 重启恢复和重复触发。

### 端到端测试

- 首次初始化；
- 登录和会话失效；
- 配置 Sub2API、同步并映射 Key；
- dry-run 生成报告；
- 三渠道组合和失败补发；
- 更新检查和升级确认流程。

## 13. 非功能目标

| 指标 | MVP 目标 |
| --- | --- |
| 部署 | amd64 Linux，Docker Compose 一条命令启动 |
| 启动 | 常规机器 10 秒内 ready，不含首次镜像拉取 |
| 人员规模 | 100 人、每人最多 10 个历史 Key |
| 报表执行 | 100 Key 在 5 分钟内完成，受 Sub2API 延迟影响 |
| 可用性 | 单实例；升级失败自动恢复旧版本和数据库备份 |
| 数据保留 | 报表至少 12 个月，审计默认 12 个月 |
| 恢复 | SQLite 数据卷可离线备份；提供升级前自动备份 |
| 浏览器 | 当前及前一主版本 Chrome、Edge、Firefox、Safari |

## 14. 明确不做

MVP 不包含：

- 多管理员、多角色和多租户；
- 多个 Sub2API 实例；
- API 请求内容或提示词审计；
- 独立移动端；
- Kubernetes Operator；
- 通用 Docker 管理面板；
- 任意脚本插件；
- 自动无确认升级；
- 使用量实时监控。

这些边界用于控制首版复杂度，不妨碍后续以模块方式扩展。

## 15. 实施顺序

1. 建立 monorepo、后端分层、React SPA 和统一构建。
2. 实现 Migrator、SQLite、Identity 和日志初始化码。
3. 实现 Sub2API 连接、用户与 Key 自动同步。
4. 实现报表聚合（用户 → Key）、快照、CSV 和手工 dry-run。
5. 实现邮箱、钉钉、飞书及组合发送。
6. 接入 Quartz 月报计划、幂等和补发。
7. 完成 Docker Compose、备份和 GitHub Release CI。
8. 实现 updater、签名验证、健康检查和自动回滚。
9. 完成安全加固、发布文档和首个稳定版本。

在线升级安排在业务闭环之后，但发布契约、数据目录和镜像标签必须从第一阶段就按升级方案设计，避免后期重构部署方式。
