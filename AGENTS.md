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

## Agent Memory

- `AGENTS.md` 是本仓库唯一的 Agent Memory 文件
- 禁止创建 `CLAUDE.md` 或其他等效的指针文件
- 应用 Backpass proposal 前必须检查其中是否包含隐私或敏感信息
- 长期指令应保持简洁，并且应适用于大多数后续开发会话

## 当前开发状态

项目目前处于架构设计和初始化阶段。在实现与验证完成之前，不得声称已经存在
可运行的正式版本。

## 文件维护

只有稳定、适用于整个仓库的约束才能写入本文件。详细设计、功能行为、运维手册
和临时实施计划应写入 `docs/`。
