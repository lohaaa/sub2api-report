# 本地开发

## 前置条件

- .NET SDK 10（具体版本由 `global.json` 控制，只需大版本为 10）；
- Node.js 24（LTS）；
- pnpm 11.17.0。

Docker 仅用于验证容器部署，本地前后端开发不依赖 Docker。

## 初始化依赖

在仓库根目录执行：

```bash
dotnet tool restore
dotnet restore Sub2ApiReport.slnx
pnpm install --frozen-lockfile
```

仓库只使用 pnpm 管理前端依赖。禁止提交 `package-lock.json` 或 `yarn.lock`。

## 启动后端

首次启动或 migration 发生变化后，先在仓库根目录执行 Migrator：

```bash
dotnet run --project src/Sub2ApiReport.Migrator
```

该命令会在被 `.gitignore` 排除的 `data/db/` 下创建本地 SQLite 数据库。Web 进程不会隐式执行 migration。

然后启动 API：

```bash
dotnet run --project src/Sub2ApiReport.Api
```

默认地址为 `http://localhost:5080`，常用端点：

- `GET /health/live`；
- `GET /health/ready`；
- `GET /api/v1/system/version`；
- `GET /openapi/v1.json`（Development 环境）。

全新数据库启动时，API 日志会输出一次性初始化码。打开 `http://localhost:5173/setup` 创建管理员；未初始化重启会使旧码失效。认证完成后可在系统设置页修改时区、Release 通道、日志级别和保留策略，这些设置来自 SQLite 且不需要重启。

需要恢复管理员密码时，先停止正在调试的 API，或保持其使用同一 SQLite 数据库，然后执行：

```bash
dotnet run --project src/Sub2ApiReport.Cli -- admin create-reset-code
```

命令只把短时恢复码输出到当前终端。随后打开 `/recover` 设置新密码；仓库不提供匿名邮件找回流程。

## 配置 Sub2API 并生成报告

升级到 0.3.0 后必须先停止 API 并运行 Migrator，应用差量 migration `AddSub2ApiAndPeople`。登录后按以下顺序操作：
1. 在系统设置的“管理员安全”确认密码，获得 10 分钟 step-up；
2. 在“Sub2API 连接”填写 Base URL 和 Admin API Key，可选填写 Codex Group ID；
3. 保存后先同步用户，再选择指定用户或“全部有效用户”；
4. 打开“API Keys”页面核对同步结果；报告生成前会自动刷新用户与 Key；
5. 在报告页面选择滚动 7 日、滚动 30 日、上一自然周、上一自然月或手工自定义区间，按 Sub2API 用户 → Key 查看用量。

各字段可在 Sub2API 管理后台获取：Base URL 是访问站点的地址（不含 `/admin`、`/api/v1`
路径）；Admin API Key 在系统设置 → 常规中创建或重新生成，生成后立即复制；用户 ID
在用户管理列表中；Codex Group ID 在分组管理列表（列设置中开启 ID 列），仅当同一个
Key 还访问其他平台时才需要填写。页面上的“获取指南”按钮提供同样说明。0.5.1 起连接保存后先同步用户，再选择指定用户或“全部有效用户”；系统按每个 Key 的所属用户携带对应 `user_id` 查询用量。

完整 Admin API Key 只在保存请求和进程内短时存在，SQLite 保存 Data Protection 密文，管理 API 只返回末四位掩码。同步响应中的完整业务 Key 字段不会写入本地数据库或日志。

## 生成报告

升级到 0.4.0 后运行 Migrator，应用 `AddReportSnapshots`。报告页面支持指定统计截止日；留空时使用配置时区中昨天，确保不包含运行当天的部分数据。每次手工生成会保存独立的 immutable canonical snapshot，不发送任何渠道。

报告引擎在生成前自动刷新用户与 Key，然后按每个 Key × 去重后的解析窗口调用 stats（`ReportConcurrency` 并发上限）。默认窗口为滚动 7/30 日、上一自然周和上一自然月；手工报告可添加自定义区间，计划配置保存在 SQLite 并在任务入队时冻结。任一区间采集失败时报告状态为部分完成并逐项列出；用户或 Key 刷新失败时整次报告终止。XLSX 工作簿由 ClosedXML 从已保存 schema v4 canonical snapshot 生成，包含“报告概览”“Key 明细”“用户汇总”“数据说明”工作表；任一区间失败时额外输出“采集异常”工作表。每个工作表包含正式 Excel Table 与筛选、冻结表头（明细表同时冻结关键标识列）、固定列宽、日期/数值格式和打印设置；不含合并单元格、图片或宏。所有不受信文本（包括以 =,+,-,@ 开头的字符串）均显式保存为文本，避免公式注入；超过 15 位的整数与 ID 按文本保存以避免精度丢失。schema v1-v3 历史快照继续可读。

## 发送渠道与报告投递

升级到 0.5.0 后运行 Migrator，应用 `AddNotificationDelivery`。在“发送渠道”页面配置邮件、
钉钉或飞书渠道；SMTP 密码与 Webhook 地址、加签密钥通过 Data Protection 加密保存，
读取接口只返回掩码。渠道保存后可用“测试”发送一条合成测试消息，不包含真实报告数据。

在报告详情页选择已启用的渠道后发送；部分完成报告需要勾选显式确认。投递按渠道隔离，
单渠道失败不阻断其他渠道；分片消息逐片记录状态，补发只重试失败渠道与失败分片。
Webhook 只接受钉钉和飞书官方 HTTPS 地址，且不跟随重定向；HTTP 200 中的业务错误码
视为失败。投递运行状态与 payload 哈希保存在 SQLite 中，用于审计与避免重复发送。


邮件正文使用按窗口组织的 HTML 汇总并附日期命名的多工作表 XLSX 工作簿；钉钉使用 Markdown 摘要，飞书使用 `post` 富文本摘要。群机器人不直接发送文件附件。系统设置的“动态配置”Tab 可配置外部 HTTP/HTTPS 访问地址、1 小时至 30 天有效期，以及 1 至 10000 次下载上限或不限制；生产环境推荐 HTTPS。配置后每条钉钉/飞书投递会冻结策略并生成独立链接。链接只在消息成功后开始计时，可在报告投递记录查看下载次数并提前撤销。报告详情的每个渠道行可在发送前预览该渠道的消息结构，预览使用当前报告数据和合成收件人/令牌。
## 计划任务与执行记录

升级到 0.7.0 后运行 Migrator。Migrator 依次应用 `AddReportScheduling` 和
`AddUnixTimeColumns` 和 `AddUnixTimeMigrationGuard`，创建 `ReportSchedules`、任务执行
扩展字段、Quartz SQLite JobStore 表、Unix 毫秒 companion 列和待完成 guard；随后在
事务中把旧 ISO 8601 TEXT 时间严格解析并回填为 UTC Unix 毫秒，同时完成 guard。
`ValidateUnixTimeBackfill` 会在任何删列前验证 guard，最后
`CompleteUnixTimeStorage` 删除旧时间列并收紧约束。任一旧值无法解析时升级终止且
回填事务回滚，API 不会启动。直接执行 `dotnet ef database update` 会因未完成 guard
失败，时间存储升级必须通过 Migrator。曾在 0.7.0 开发阶段
应用旧 `AddReportScheduling` migration 的本地数据库会先验证调度 schema，再迁移其
history 标识，不会重复创建调度表。API 自身只校验 schema 并对账 trigger，不隐式执行
migration。

计划任务页可配置每月 1-28 日、`HH:mm` 时间和 IANA 时区，默认每月 1 日 09:00
`Asia/Shanghai`。启用后运行时使用全部已启用发送渠道；修改配置会立即重建持久化
trigger，页面显示同步状态与下次运行时间。应用错过运行时间后只补一次，不连续补发
所有错过的月份。

“立即运行”和自动触发都会先创建规范化执行记录，再异步进入采集、快照和投递阶段。
失败重试创建关联原执行的新尝试，不覆盖历史结果。部分报告保存快照但不自动发送；
进程重启后无法确认结果的渠道标记为 `outcome_unknown`，必须在计划任务页显式确认后
重试，普通渠道补发不会静默重发这些记录。

## 启动前端

在另一个终端执行：

```bash
pnpm --dir web run dev
```

也可以进入 `web/` 后执行 `pnpm run dev`。Vite 默认监听 `http://localhost:5173`，并把 `/api`、`/health` 和 `/openapi` 代理到 ASP.NET Core。

## 质量检查

完整验收统一执行：

```bash
pnpm quality
```

`.NET` 质量门可单独执行：

```bash
pnpm quality:dotnet
```

该命令依次验证格式、Release 构建和全部 `.NET` 测试。Agent 环境提供 LSP 时，可先对
相关文件做一次诊断，仅作为辅助，不属于强制质量门。

前端质量门可单独执行：

```bash
pnpm quality:web
```

它依次运行 TypeScript typecheck、oxlint、Vitest 和生产构建。前端生产构建输出到 `src/Sub2ApiReport.Api/wwwroot/`。该目录是生成物，不提交到 Git；ASP.NET Core 发布和 App Dockerfile 会包含这份产物。

## 变更日志和发布

面向用户的重要变更先记录在根目录 `CHANGELOG.md` 的 `Unreleased` 章节。首次公开版本固定为 `v1.0.0`；在此之前不创建 `0.x` Tag 或 GitHub Release。

发布前可在隔离的 linux/amd64 环境使用测试密钥生成本地 bundle；该操作不创建 Candidate workflow 或 GitHub Release：

```bash
RELEASE_SIGNING_KEY_FILE=/absolute/path/to/test-release-key.pem \
RELEASE_NOTES_SECTION=Unreleased \
deploy/build-release-assets.sh 1.2.1-internal.1 /tmp/sub2api-report-release-test
```

准备正式版本时：

1. 将 `Unreleased` 条目整理到 `## [X.Y.Z] - YYYY-MM-DD` 章节；
2. 统一更新 .NET 和根/前端 Node 项目版本；
3. 运行 `deploy/extract-release-notes.sh CHANGELOG.md X.Y.Z /tmp/release-notes.md`；
4. 完成本地质量、签名 bundle、全新安装和适用的 N-1 迁移/回滚验收；
5. 仅在全部门通过后创建并推送 `vX.Y.Z` Tag。

Release workflow 不自动拼接 Git 提交信息。对应版本章节缺失、为空或版本不一致时，发布会在构建镜像前失败。

## Docker Compose

源码仓库使用开发 override 本地构建 App：

```bash
cd deploy
cp .env.example .env
./dev-up.sh
```

正式 `install.sh` 只接受 GitHub Release bundle 中已经校验的离线镜像归档，不从源码构建，也不访问公共 Registry。

正式和开发 Compose 都只有 App，任何容器都不挂载 Docker Socket。`dev-up.sh` 只生成端口和监听地址等本地 `.env` 并启动 App；当前开发机没有 Docker 时，仍可完成全部非容器构建和测试。
