# Sub2API Report

Sub2API Report 是一个公开、单管理员、单实例的 Codex API Key 用量报告系统。它从 Sub2API 自动同步用户与 API Key，按用户 → Key 聚合完整自然日用量，保存不可变报告快照，并通过邮件、钉钉和飞书定时投递。

## 主要能力

- 自动同步 Sub2API 用户和 API Key，稳定标识为 `user_id + api_key_id`；
- 滚动 7/30 日、上一自然周、上一自然月和手工自定义区间；
- schema v4 不可变快照、UTF-8 BOM CSV 和历史快照读取兼容；
- 邮件、钉钉、飞书组合投递、分片、逐渠道状态和失败补发；
- Quartz 持久化月报计划、窗口冻结、幂等、任务级重试和重启恢复；
- 单管理员安全初始化、Cookie 会话、CSRF、step-up 和主机恢复码；
- SQLite 动态配置和 Data Protection Secret 加密；
- 签名 GitHub Release bundle 安装，不依赖公共容器 Registry；
- 管理页面 App-only 在线升级、SQLite 一致性备份、健康验证和自动回滚。

## 系统要求

正式发行版仅支持：

- Linux `amd64`；
- Docker Engine；
- Docker Compose v2；
- OpenSSL、jq、gzip 和 sha256sum；
- 可访问 GitHub Release Assets。

不支持 rootless Docker、Podman、Swarm、Kubernetes 和 arm64。建议至少准备 2 vCPU、2 GiB 内存及足够保存镜像、数据库备份和报告的磁盘空间。

## 安装

从 [GitHub Releases](https://github.com/lohaaa/sub2api-report/releases) 下载完整 bundle 和 `checksums.txt`：

```bash
VERSION=1.0.0
curl -fsSLO "https://github.com/lohaaa/sub2api-report/releases/download/v${VERSION}/sub2api-report-v${VERSION}-linux-amd64.tar.gz"
curl -fsSLO "https://github.com/lohaaa/sub2api-report/releases/download/v${VERSION}/checksums.txt"
grep "sub2api-report-v${VERSION}-linux-amd64.tar.gz$" checksums.txt | sha256sum -c -
mkdir -p sub2api-report
tar -xzf "sub2api-report-v${VERSION}-linux-amd64.tar.gz" -C sub2api-report
cd sub2api-report
sudo ./install.sh
```

安装脚本会再次校验 Release 签名、归档 SHA-256、大小、架构、版本和镜像 ID，然后执行 `docker load` 并安装到 `/opt/sub2api-report`。生产服务器不会从公共容器 Registry 拉取镜像。

已安装 GitHub CLI 时可以额外验证构建证明：

```bash
gh attestation verify "sub2api-report-v${VERSION}-linux-amd64.tar.gz" --repo lohaaa/sub2api-report
```

默认访问地址为 `http://<server>:8080`。如需修改监听地址或端口，在首次安装前编辑 bundle 中的 `.env.example`。

> 不要使用未经校验的 `curl | sh` 安装方式。

## 首次初始化

安装完成后读取 App 日志中的一次性管理员初始化码：

```bash
cd /opt/sub2api-report
sudo docker compose logs app
```

打开系统页面，使用初始化码创建唯一管理员。初始化码具有有效期，成功创建管理员后立即失效。

管理员忘记密码时，在部署主机生成一次性恢复码：

```bash
cd /opt/sub2api-report
sudo docker compose exec app appctl admin create-reset-code
```

恢复码只应出现在当前主机终端，不得写入 Issue、聊天记录或脚本。

## 基本配置

登录后按以下顺序配置：

1. 在系统设置中确认时区、报告保留和下载链接策略；
2. 配置 Sub2API 地址和 Admin API Key，执行连接测试；
3. 同步用户和 API Key，并选择报告用户范围；
4. 配置邮箱、钉钉或飞书渠道并发送测试消息；
5. 配置月报日期、时间、窗口和启用状态；
6. 先手工生成报告，确认统计和 CSV 后再启用计划投递。

业务配置和凭证保存在 SQLite，凭证使用持久化 Data Protection key ring 加密。运行期可变配置不依赖环境变量。

## 备份与恢复

应用内升级会自动创建 SQLite 一致性备份，执行 integrity check 和 SHA-256 后才替换 App。主机 `update.sh` 同样会在迁移前创建独立数据库备份。

生产环境还应定期执行主机完整备份，覆盖 SQLite、Data Protection keys 和报告文件。完整命令和恢复步骤见 [Docker 部署与运维方案](docs/deployment.md#10-备份)。备份、数据库和报告不得上传到公开仓库或 Actions artifact。

## 更新

### App-only 在线升级

管理员可以在“系统更新”页面检查签名稳定版。安装操作要求重新输入当前密码。Updater 会：

1. 校验固定 GitHub 仓库、RSA 签名、版本、架构和 deployment contract；
2. 流式下载并校验 App 镜像归档；
3. 暂停新报告任务并创建 SQLite 一致性备份；
4. 通过 Docker Engine API 替换 App；
5. 连续验证进程、版本、数据库和内部握手；
6. 失败时恢复旧镜像和升级前数据库。

App 容器永远不挂载 Docker Socket。只有无主机端口的 Updater 可以访问 Socket，并且只管理当前 instance ID 的 App 容器。

### 部署契约更新

当 Release 修改 Updater、Compose、端口、数据卷或权限时，页面会要求手工升级。下载新版本完整 bundle 后执行：

```bash
cd /path/to/new-bundle
sudo ./update.sh
```

脚本保留现有 `.env`、内部 token、instance ID 和数据卷，并在失败时恢复旧部署。

## 开发

需要 .NET 10 SDK、Node.js 和 pnpm。安装依赖并运行完整质量门：

```bash
pnpm install --frozen-lockfile
pnpm quality
```

本地启动后端和前端：

```bash
dotnet run --project src/Sub2ApiReport.Api
pnpm dev:web
```

源码 Docker Compose 开发环境：

```bash
cd deploy
cp .env.example .env
./dev-up.sh
```

开发和测试必须使用系统临时目录中的隔离 SQLite 与 Data Protection 路径。更多信息见 [本地开发](docs/development.md)。

## 文档

- [变更日志](CHANGELOG.md)
- [Docker 部署与运维方案](docs/deployment.md)
- [在线升级架构](docs/online-update.md)
- [Updater 威胁模型](docs/updater-threat-model.md)
- [系统架构](docs/architecture.md)
- [配置管理策略](docs/configuration.md)
- [实施计划](docs/implementation-plan.md)
- [公开仓库数据政策](docs/public-repository-policy.md)

## 安全

这是公开仓库。代码、测试、日志、截图、Issue 和讨论中不得包含真实身份、内部地址、凭证、生产数据库、报告或浏览器追踪文件。

发现未修复漏洞时，请按 [安全政策](SECURITY.md) 使用 GitHub Private Vulnerability Reporting，不要创建公开 Issue。仓库已启用 Secret Protection 和 Push protection。

## License

Sub2API Report 使用 [Apache License 2.0](LICENSE)。
