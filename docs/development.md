# 本地开发

## 前置条件

- .NET SDK 10（具体版本由 `global.json` 控制，只需大版本为 10）；
- Node.js 22；
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
5. 在报告页面手工生成 7/30 日报告，按 Sub2API 用户 → Key 查看用量。

各字段可在 Sub2API 管理后台获取：Base URL 是访问站点的地址（不含 `/admin`、`/api/v1`
路径）；Admin API Key 在系统设置 → 常规中创建或重新生成，生成后立即复制；用户 ID
在用户管理列表中；Codex Group ID 在分组管理列表（列设置中开启 ID 列），仅当同一个
Key 还访问其他平台时才需要填写。页面上的“获取指南”按钮提供同样说明。0.5.1 起连接保存后先同步用户，再选择指定用户或“全部有效用户”；系统按每个 Key 的所属用户携带对应 `user_id` 查询用量。

完整 Admin API Key 只在保存请求和进程内短时存在，SQLite 保存 Data Protection 密文，管理 API 只返回末四位掩码。同步响应中的完整业务 Key 字段不会写入本地数据库或日志。

## 生成报告

升级到 0.4.0 后运行 Migrator，应用 `AddReportSnapshots`。报告页面支持指定统计截止日；留空时使用配置时区中昨天，确保不包含运行当天的部分数据。每次手工生成会保存独立的 immutable canonical snapshot，不发送任何渠道。

报告引擎在生成前自动刷新用户与 Key，然后对每个 Key 使用其所属 `user_id` 直接调用 7 日与 30 日 stats（`ReportConcurrency` 并发上限）。任一区间采集失败时报告状态为部分完成并逐项列出；用户或 Key 刷新失败时整次报告终止，错误记录在“最近生成记录”中展示。CSV 从已保存快照生成，使用 UTF-8 BOM，并对可能触发电子表格公式的文本加前缀保护。

## 发送渠道与报告投递

升级到 0.5.0 后运行 Migrator，应用 `AddNotificationDelivery`。在“发送渠道”页面配置邮件、
钉钉或飞书渠道；SMTP 密码与 Webhook 地址、加签密钥通过 Data Protection 加密保存，
读取接口只返回掩码。渠道保存后可用“测试”发送一条合成测试消息，不包含真实报告数据。

在报告详情页选择已启用的渠道后发送；部分完成报告需要勾选显式确认。投递按渠道隔离，
单渠道失败不阻断其他渠道；分片消息逐片记录状态，补发只重试失败渠道与失败分片。
Webhook 只接受钉钉和飞书官方 HTTPS 地址，且不跟随重定向；HTTP 200 中的业务错误码
视为失败。投递运行状态与 payload 哈希保存在 SQLite 中，用于审计与避免重复发送。

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

## Docker Compose

当前 Compose 包含 App 和无安装权限的 Updater 骨架：

```bash
cd deploy
cp .env.example .env
./install.sh
```

Updater 在在线升级安全边界完成前不会挂载 Docker Socket，状态接口明确返回安装未启用。当前开发机没有 Docker 时，仍可完成全部非容器构建和测试。
