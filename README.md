# Sub2API Report

Sub2API Report 是一个面向单管理员部署的 Codex API Key 用量报告工具。它从 Sub2API 读取按 Key 聚合的用量，将 Key 映射到人员，并按计划通过邮箱、钉钉和飞书发送 7 天与 30 天报告。

> 项目当前处于 0.4.0 报告引擎阶段，已支持手工生成 7/30 日不可变报告快照和 CSV；发送渠道、计划任务与正式发布尚未实现。

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
