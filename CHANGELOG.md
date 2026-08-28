# Changelog

本项目的所有重要变更都记录在此文件中。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

## [0.7.0] - 2026-08-28

### Added

- 增加持久化报告计划、Quartz 月报调度、窗口冻结、规范化执行记录和任务级重试。
- 增加滚动 7/30 日、上一自然周、上一自然月和手工自定义完整自然日窗口。
- 增加 schema v4 不可变报告快照、UTF-8 BOM 动态 CSV 和限时下载授权。
- 增加邮件、钉钉和飞书组合投递、逐渠道状态、失败补发与结果审计。
- 增加 GitHub Actions 质量门和签名 Release workflow。
- 增加不依赖公共容器 Registry 的 linux/amd64 离线镜像归档、完整安装 bundle、SBOM 和 artifact attestation。
- 增加生产 bundle 的签名校验、一键安装和手工部署契约更新脚本。
- 项目采用 Apache License 2.0，并在源码、容器镜像和 Release bundle 中携带许可证。

### Changed

- 报告统计主体统一为 Sub2API 用户到 API Key，稳定标识使用 `user_id + api_key_id`。
- 生产 Docker Compose 只加载经过校验的本地镜像，源码开发使用独立 Compose override 构建。

### Removed

- 移除人员档案和人员到 API Key 归属功能，历史报告快照保持不可变。

### Security

- Release manifest 使用独立 RSA 发布密钥签名，并校验归档 SHA-256、大小、架构、版本和镜像 ID。
- 手工部署契约更新在迁移前创建独立数据库备份，并在失败回滚前验证备份哈希。
- 应用内在线安装继续保持关闭；Updater 不挂载 Docker Socket。
- 增加公开安全政策，并将未修复漏洞引导至 GitHub Private Vulnerability Reporting。

[Unreleased]: https://github.com/lohaaa/sub2api-report/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/lohaaa/sub2api-report/releases/tag/v0.7.0
