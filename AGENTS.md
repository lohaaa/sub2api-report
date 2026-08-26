# AGENTS.md

## 项目说明

Sub2API Report 是一个公开的单管理员报告系统，用于按照 API Key 归属人聚合
Sub2API Codex 用量，并定时发送报告。

已确认的技术架构如下：

- 后端使用 .NET 10 和 ASP.NET Core 模块化单体架构
- 前端使用 React、TypeScript、Vite 和 shadcn/ui
- ASP.NET Core 在同一个应用容器中提供 SPA 和 API
- 使用 SQLite 持久化数据，使用 Quartz.NET 执行计划任务
- 使用 Docker Compose 部署，目标平台为 linux/amd64
- 使用独立的内部 Updater 实现签名在线升级和失败回滚
- 可在运行期修改的业务和运维配置必须存入 SQLite 并动态生效；环境变量和
  配置文件只允许承载数据库连接、监听端口、进程间 Secret 等启动闭环配置

进行架构变更前必须先阅读 `docs/README.md`。文档存在冲突时，按照其中定义的
文档权威顺序执行。

## 官方 Skills

- .NET SDK、ASP.NET Core、EF Core 和 .NET 测试任务必须优先使用仓库中对应的
  `dotnet/skills` 官方 Skill
- shadcn/ui 组件、样式、表单和项目配置任务必须使用官方 `shadcn` Skill
- 通用前端任务还必须遵守 `modern-web-guidance` Skill
- 只按任务加载必要 Skill，禁止为了方便一次性引入无关 Skill

## 公开仓库安全

本仓库是公开仓库。禁止添加私人聊天记录、真实姓名、个人邮箱、组织标识、
内部主机信息、凭证、生产报告或生产日志。示例和测试必须使用合成数据与保留的
示例域名。

必须遵守 `docs/public-repository-policy.md`。Secret、SQLite 文件、生成的报告、
备份、日志、浏览器追踪文件和 Backpass 证据不得提交到 Git。

启动 API、Migrator 或测试做验证前，必须通过启动配置显式传入仓库内隔离的
SQLite 与 Data Protection 绝对路径；不得让 IDE Content Root、当前目录或应用回退
逻辑决定数据目录。

执行 EF migration scaffold、remove 或 database update 前，必须确认设计时程序集为
最新并将连接串指向一次性 SQLite 数据库；禁止让任何破坏性 migration 操作以真实
或持久化开发数据库为目标。

运行开发、构建、测试或诊断工具时，临时目录和日志目录必须指向系统临时目录；
辅助工具应隔离安装并通过绝对路径调用，禁止为验收修改全局 PATH 或执行全局安装；
完成后扫描仓库根目录及父目录，清理误生成的 `data/`、数据库、key ring、时间戳
目录、binlog、trace、截图和其他非预期产物。

## 质量验收

涉及 C# 的代码验收必须执行 `pnpm quality:dotnet`；该命令必须包含格式检查、Release
构建、JetBrains InspectCode 的 `SUGGESTION` 及以上检查和全部 .NET 测试，任何阶段
出现问题都不得宣称完成。完整里程碑和提交前必须执行 `pnpm quality`，覆盖后端和前端
全部质量门。

## Agent Memory

- `AGENTS.md` 是本仓库唯一的 Agent Memory 文件
- 禁止创建 `CLAUDE.md` 或其他等效的指针文件
- 应用 Backpass proposal 前必须检查其中是否包含隐私或敏感信息
- 长期指令应保持简洁，并且应适用于大多数后续开发会话

## 当前开发状态

项目目前处于 0.4.0 报告引擎阶段，已实现安全初始化、Sub2API Key 同步与有效期
归属、7/30 日手工报告、不可变快照和 UTF-8 BOM CSV。发送渠道、计划任务和在线
升级尚未实现，不得声称已经存在可运行的正式版本。前端依赖统一使用 pnpm，禁止
生成 npm 或 Yarn 锁文件。

## 文件维护

只有稳定、适用于整个仓库的约束才能写入本文件。详细设计、功能行为、运维手册
和临时实施计划应写入 `docs/`。
