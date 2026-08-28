# Sub2API Report

Sub2API Report 是一个面向单管理员部署的 Codex API Key 用量报告工具。它从 Sub2API 读取账号下每个 API Key 的用量，按 Sub2API 用户 → Key 分层展示可配置统计窗口，并可通过邮箱、钉钉和飞书发送报告。

> 项目当前处于 0.7.0 持久化计划任务阶段：报告支持滚动 7/30 日、上一自然周、上一自然月和手工自定义区间，生成 schema v4 不可变快照与动态 CSV；计划窗口保存在 SQLite 并在 Quartz 任务入队时冻结。规范化执行记录、任务级重试、逐渠道状态与失败补发已经实现。在线升级与正式发布尚未实现。

## 设计目标

- Docker Compose 一键部署
- .NET 10 后端
- React、TypeScript、Vite 和 shadcn/ui 前端
- 前后端同源、一体化 App 容器
- SQLite 零依赖持久化
- 可变配置通过管理页面写入 SQLite 并在运行期动态生效
- 单管理员账户
- 首次启动使用 Docker 日志一次性初始化码创建管理员
- 邮箱、钉钉、飞书任意组合发送
- GitHub Release 和 GHCR 发布
- 管理页面一键升级、健康检查和失败回滚
- linux/amd64

## 文档

- [本地开发](docs/development.md)
- [系统架构与技术方案](docs/architecture.md)
- [配置管理策略](docs/configuration.md)
- [Docker 部署与运维方案](docs/deployment.md)
- [在线升级架构](docs/online-update.md)
- [实施计划](docs/implementation-plan.md)
- [Sub2API 数据源调查](docs/technical-investigation.md)
- [公开仓库数据政策](docs/public-repository-policy.md)

## 项目边界

首版是单实例模块化单体，不提供多租户、多管理员、通用 Docker 管理、实时用量监控或请求内容审计。详细范围见系统架构文档。

## 隐私与安全

这是公开仓库。代码、文档、测试、截图、日志和 Issue 中不得包含真实身份、内部地址、凭证、聊天记录或生产报告。所有示例和测试必须使用合成数据。

发现安全问题时，不要在公开 Issue 中提交凭证、数据库、报告或可识别的运行日志。

## License

License 尚未确定，在选定开源许可证前不发布稳定版本。
