# Documentation

## Architecture

- [本地开发](development.md)：工具链、启动命令、质量检查和本地数据约束。
- [系统架构与技术方案](architecture.md)：产品边界、技术栈、模块、数据模型、API、前端和安全基线。
- [配置管理策略](configuration.md)：数据库动态配置、启动配置例外、生效语义和代码约束。
- [Docker 部署与运维方案](deployment.md)：Compose 拓扑、数据卷、初始化、备份和运行约束。
- [服务器直接部署](server-deployment.md)：无 Docker 的 self-contained systemd 安装、更新和回滚。
- [实施计划](implementation-plan.md)：里程碑、发布门槛、CI 矩阵和 Definition of Done。

## Research

- [Sub2API 数据源调查](technical-investigation.md)：可用接口、统计口径和发送渠道调查。

## Repository Policy

- [公开仓库数据政策](public-repository-policy.md)：禁止提交的敏感信息和仓库保护要求。

## Document Authority

发生冲突时按以下顺序执行：

1. `architecture.md` 和 `configuration.md`
2. `deployment.md` 和 `server-deployment.md`
3. `technical-investigation.md`

数据源调查记录事实与早期分析，最终工程决策以架构文档为准。
