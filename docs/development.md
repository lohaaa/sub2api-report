# 本地开发

## 前置条件

- .NET SDK 10.0.302；
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

## 配置 Sub2API 和人员归属

升级到 0.3.0 后必须先停止 API 并运行 Migrator，应用差量 migration `AddSub2ApiAndPeople`。登录后按以下顺序操作：

1. 在系统设置的“管理员安全”确认密码，获得 10 分钟 step-up；
2. 在“Sub2API 连接”填写 Base URL、Admin API Key、目标用户 ID 和可选 Codex Group ID；
3. 保存后执行连接测试；
4. 打开“人员与 Key”，同步全部上游分页；
5. 创建人员并为每个 Key 配置包含起止日的归属，直到未映射数量为 0。

完整 Admin API Key 只在保存请求和进程内短时存在，SQLite 保存 Data Protection 密文，管理 API 只返回末四位掩码。同步响应中的完整业务 Key 字段不会写入本地数据库或日志。

## 生成报告

升级到 0.4.0 后运行 Migrator，应用 `AddReportSnapshots`。报告页面支持指定统计截止日；留空时使用配置时区中昨天，确保不包含运行当天的部分数据。每次手工生成会保存独立的 immutable canonical snapshot，不发送任何渠道。

报告引擎把 30 日窗口按 7 日边界和 Key 归属有效期切分，以数据库中的 `ReportConcurrency` 并发上限调用 Sub2API stats。任一区间采集失败、归属冲突或存在实际用量但没有归属时，报告状态为部分完成。CSV 从已保存快照生成，使用 UTF-8 BOM，并对可能触发电子表格公式的文本加前缀保护。

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

该命令依次验证格式、Release 构建、JetBrains InspectCode 和全部 `.NET` 测试。InspectCode 使用仓库锁定的官方 `JetBrains.ReSharper.GlobalTools`，检查级别为 `SUGGESTION` 及以上；发现任何 Rider/ReSharper 问题时返回非零。SARIF 报告和缓存只写入系统临时目录并在结束时自动删除。

前端质量门可单独执行：

```bash
pnpm quality:web
```

它依次运行 TypeScript typecheck、oxlint、Vitest、生产构建和桌面/移动 Playwright。前端生产构建输出到 `src/Sub2ApiReport.Api/wwwroot/`。该目录是生成物，不提交到 Git；ASP.NET Core 发布和 App Dockerfile 会包含这份产物。

## Docker Compose

当前 Compose 包含 App 和无安装权限的 Updater 骨架：

```bash
cd deploy
cp .env.example .env
./install.sh
```

Updater 在在线升级安全边界完成前不会挂载 Docker Socket，状态接口明确返回安装未启用。当前开发机没有 Docker 时，仍可完成全部非容器构建和测试。
