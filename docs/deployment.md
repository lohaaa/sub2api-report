# Docker 部署与运维

本文描述正式 Release 的 Docker Compose 部署。目标平台为 Linux `amd64`，主机需要 Docker Engine、Docker Compose v2、`curl`、`flock`、`jq`、OpenSSL、`gzip`、`tar` 和 `sha256sum`。生产部署不依赖公共容器 Registry。

## 1. 部署契约

Deployment contract v2 是 App-only 拓扑：

```text
Host :8081 -> App :8080 -> SQLite /data/db/sub2api-report.db
                         -> Data Protection /data/keys
```

生产 Compose 只有 `app` 服务和 `app-data` 命名卷。App 同时提供 SPA、API 和 Quartz 任务，不挂载 `/var/run/docker.sock`，没有控制网络、常驻更新进程或进程间更新凭证。

权威契约为 [`deploy/upgrade-contract.json`](../deploy/upgrade-contract.json)：

- architecture：`linux/amd64`；
- 本地激活标签：`sub2api-report-app:current`；
- 更新方法：宿主机 `bootstrap.sh`；
- 更新前必须备份数据库；
- 健康验证失败必须自动回滚。

## 2. Release bundle

正式 Docker bundle 结构如下：

```text
compose.yaml
.env.example
upgrade-contract.json
release-manifest.json
release-manifest.sig
update-public-key.pem
CHANGELOG.md
RELEASE-NOTES.md
LICENSE
bootstrap.sh
install.sh
update.sh
release-lib.sh
appctl
images/
  sub2api-report-app-linux-amd64.tar.gz
  checksums.txt
```

Release manifest schema v4 只描述 App 镜像、数据库迁移目标和 Release notes。构建流程签名 manifest，外层 `checksums.txt` 校验下载资产；bundle 解压后还会验证 RSA 签名、App archive SHA-256、大小、唯一镜像标签、OCI config/target digest、平台、版本、角色和 contract 标签，然后才执行 `docker load`。

## 3. 安装和更新

首次安装和以后更新到最新正式版使用同一条命令：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | bash
```

bootstrap 以当前用户解析 GitHub Release、下载并校验 bundle，只在安装主机依赖、写入安装目录和操作 Docker 时调用 `sudo`。默认安装目录为 `/opt/sub2api-report`，默认监听 `0.0.0.0:8081`。

可选参数：

| 参数 | 默认值 | 作用 |
| --- | --- | --- |
| `SUB2API_REPORT_VERSION` | `latest` | 固定安装指定正式版本，例如 `1.2.0` |
| `SUB2API_REPORT_INSTALL_DIR` | `/opt/sub2api-report` | Compose、`.env` 和发布元数据目录 |
| `SUB2API_REPORT_PORT` | 新安装为 `8081` | 主机端口；更新时省略会保留当前值 |
| `SUB2API_REPORT_BIND_ADDRESS` | 新安装为 `0.0.0.0` | 监听地址；更新时省略会保留当前值 |
| `SUB2API_REPORT_START` | `true` | `false` 表示安装或更新验证后保持 App 停止 |

仅监听本机的示例：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | \
  SUB2API_REPORT_PORT=18080 \
  SUB2API_REPORT_BIND_ADDRESS=127.0.0.1 \
  bash
```

只准备部署、不保持运行：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | \
  SUB2API_REPORT_START=false bash
```

更新没有管理页面入口，也不接受应用内触发。日常更新只需再次执行无参数 bootstrap 命令。

## 4. 初始化

首次启动后，从 App 日志读取一次性管理员初始化码：

```bash
cd /opt/sub2api-report
sudo docker compose logs --no-log-prefix app 2>&1 | \
  grep -F "One-time setup code"
```

初始化码默认有效 30 分钟。打开 `http://<服务器地址>:8081` 创建管理员，然后在管理页面配置 Sub2API、报告渠道和月报计划。

## 5. 运行配置

安装目录 `.env` 只保存启动闭环参数：

```dotenv
APP_PORT=8081
BIND_ADDRESS=0.0.0.0
SECURE_COOKIES=false
```

Sub2API 地址和密钥、渠道、计划、日志级别、报告保留策略等运行期配置必须保存在 SQLite 并由管理页面修改。不要通过 Compose 环境变量维护业务配置。

使用 HTTPS 反向代理时，将 `BIND_ADDRESS` 限制为 `127.0.0.1`，并在确认代理正确传递 HTTPS 后设置 `SECURE_COOKIES=true`。不要直接暴露 `/data` 或安装目录。

## 6. 数据与备份

正式数据位于 `app-data` 命名卷：

```text
/data/db/sub2api-report.db
/data/keys/
```

`docker compose down` 不删除命名卷；`docker compose down --volumes` 会删除业务数据，生产环境不得作为普通停止命令使用。

完整更新会停止 App，并用当前已安装镜像把 `/data/db` 归档到：

```text
/opt/sub2api-report/data-backups/<UTC timestamp>/db.tar
/opt/sub2api-report/data-backups/<UTC timestamp>/checksums.txt
```

同时保存更新前控制文件：

```text
/opt/sub2api-report/deploy-backups/<UTC timestamp>/
```

自动回滚前会严格验证备份 checksum，然后恢复旧控制文件、旧 App 标签和数据库。无论更新成功或失败，脚本都保留备份，不自动清理。

主机级备份至少应包含业务数据卷、`/opt/sub2api-report/data-backups` 和 `/opt/sub2api-report/deploy-backups`。数据库、备份、日志和密钥不得上传到公开仓库或 Issue。

## 7. v1 到 v2 迁移

v1.1.x Docker 部署包含 App 和旧 Updater。执行同一条无参数 bootstrap 命令会完成一次性迁移：

1. 从实际容器读取旧 App image ID，并记录可选的旧 Updater 容器和 image ID；
2. 备份 `.env`、Compose、发布控制文件和 SQLite；
3. 停止旧 App 和旧 Updater，但在新 App 通过健康检查前保留旧 Updater 容器和镜像；
4. 加载并验证 v2 App，写入 App-only Compose，启动新 App；
5. 新 App 健康后才移除旧 Updater 容器；
6. 任一步骤失败都恢复旧 Compose、App/Updater 标签和数据库，并重新启动旧双服务。

成功迁移不会自动删除旧 `updater-state` 卷、`secrets/updater-token` 或历史备份。确认 v2 App、月报计划和投递均正常后，管理员可按本机实际名称手工归档或清理这些不再使用的遗留数据；迁移脚本不会执行不可逆清理。

## 8. 服务管理

```bash
cd /opt/sub2api-report
sudo docker compose ps
sudo docker compose logs -f app
sudo docker compose restart app
sudo docker compose stop app
sudo docker compose up -d app
sudo docker compose down
```

也可以使用安装目录中的 `appctl`：

```bash
sudo /opt/sub2api-report/appctl status
sudo /opt/sub2api-report/appctl logs
sudo /opt/sub2api-report/appctl restart
```

健康端点：

- `/health/live`：进程存活；
- `/health/ready`：数据库迁移完成并可接收请求。

Compose healthcheck 使用 liveness；安装、更新和反向代理验收还必须检查 readiness。

## 9. 故障处理

查看状态和最近日志：

```bash
cd /opt/sub2api-report
sudo docker compose ps
sudo docker compose logs --tail 200 app
```

常见检查项：

- 主机端口是否被其他进程占用；
- `app-data` 是否可读写且磁盘空间充足；
- `.env` 中端口、监听地址和 Secure Cookie 是否与代理一致；
- `/health/live` 与 `/health/ready` 是否返回成功；
- 更新失败提示中的备份路径和旧部署恢复结果。

不要在失败后手工删除 `data-backups`、`deploy-backups`、旧镜像或遗留 v1 状态卷。先确认旧版本已恢复健康，再根据日志和 checksum 进行人工处理。

## 10. 发布资产

v1.2.0 及后续 App-only Release 至少包含：

```text
sub2api-report-app-v1.2.0-linux-amd64.tar.gz
sub2api-report-v1.2.0-linux-amd64.tar.gz
sub2api-report-server-v1.2.0-linux-amd64.tar.gz
release-manifest.json
release-manifest.sig
upgrade-contract.json
update-public-key.pem
release-notes-v1.2.0.md
CHANGELOG.md
LICENSE
checksums.txt
sub2api-report-app-v1.2.0.spdx.json
```

Release workflow 需要 Actions secret `RELEASE_SIGNING_KEY_PEM`。私钥只能进入 Tag 发布 Job 的临时文件；公钥随 bundle 发布。密钥变化默认被现有安装拒绝，轮换必须独立公告并由管理员显式确认。

Tag 版本必须与 `Directory.Build.props`、根/前端 `package.json` 和 `CHANGELOG.md` 对应章节一致。Release 页面说明和随包 notes 都从该 Changelog 章节生成。
