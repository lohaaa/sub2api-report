# Docker 部署与运维方案

- 状态：设计基线
- 目标平台：Linux amd64 + Docker Engine + Docker Compose v2

## 1. 一键部署目标

首次部署要求主机安装 Docker Engine、Docker Compose v2、OpenSSL、jq、gzip 和 sha256sum。发行包提供：

```text
deploy-bundle/
├─ compose.yaml
├─ .env.example
├─ bootstrap.sh
├─ install.sh
├─ update.sh
├─ release-lib.sh
├─ appctl
├─ upgrade-contract.json
├─ CHANGELOG.md
├─ LICENSE
├─ RELEASE-NOTES.md
├─ release-manifest.json
├─ release-manifest.sig
├─ update-public-key.pem
└─ images/
   ├─ sub2api-report-app-linux-amd64.tar.gz
   ├─ sub2api-report-updater-linux-amd64.tar.gz
   └─ checksums.txt
```

### 1.1 服务器一键部署

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | sudo bash
```

官方 bootstrap 自动解析最新 Release，安装所需的小型宿主工具，下载完整 bundle 和 checksums，校验 SHA-256 后调用 bundle 中的 `install.sh`。已有 `/opt/sub2api-report` 安装时，同一命令会调用新 bundle 的 `update.sh`。Docker Engine 和 Docker Compose v2 必须预先安装并运行。

### 1.2 Docker Compose 手动部署

让 bootstrap 只准备文件和镜像：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | \
  sudo SUB2API_REPORT_START=false bash
```

再显式启动：

```bash
cd /opt/sub2api-report
sudo docker compose up -d
sudo docker compose ps
sudo docker compose logs -f app
```

需要固定版本时传入环境变量：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | \
  sudo SUB2API_REPORT_VERSION=1.0.0 bash
```

手工下载、checksum 和 attestation 校验方式见 README。生产主机不从公共 Registry 拉取镜像，部署用户不持有发布私钥；bundle 内只包含用于验签的公钥。

安装主机需要 Docker Engine、Docker Compose v2、OpenSSL、jq、gzip 和 sha256sum。脚本默认安装到 `/opt/sub2api-report`；可通过 `SUB2API_REPORT_INSTALL_DIR` 显式覆盖。后续部署契约升级在新 bundle 目录执行 `sudo ./update.sh`，脚本保留现有 `.env`、内部 token、实例 ID 和数据卷，并在停止 App 后将数据库一致性副本写到安装目录的 `data-backups/`，与 Docker data volume 分离。重复安装同一版本或降级默认被拒绝。

安装完成后通过以下命令读取一次性管理员初始化码：

```bash
cd /opt/sub2api-report
sudo docker compose logs app
```

## 2. Compose 拓扑

```text
services:
  app:
    image: sub2api-report-app:current
    pull_policy: never
    ports:
      - "${BIND_ADDRESS:-0.0.0.0}:${APP_PORT:-8080}:8080"
    volumes:
      - app-data:/data
      - ./secrets/updater-token:/run/secrets/updater-token:ro
    networks:
      - frontend
      - control

  updater:
    image: sub2api-report-updater:bootstrap
    pull_policy: never
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - app-data:/managed-data
      - updater-state:/update-state
      - ./secrets/updater-token:/run/secrets/updater-token:ro
    networks:
      - control
    # no ports

networks:
  frontend:
  control:
    internal: true

volumes:
  app-data:
  updater-state:
```

实际 Compose 文件还必须包含固定项目标签、healthcheck、安全选项和资源限制。以上仅用于说明拓扑，不是最终可执行文件。

## 3. 容器职责

### App

- ASP.NET Core API；
- React SPA 静态文件；
- Quartz 计划任务；
- SQLite 数据和报告文件；
- 对外访问 Sub2API、SMTP、钉钉和飞书；
- 通过内网调用 updater；
- 不挂载 Docker Socket。

### Updater

- 检查和验证签名 release；
- 下载并校验 App 镜像压缩归档；
- 使用 Docker API 加载并替换当前项目 App 容器；
- 管理升级状态和自动回滚；
- 不对宿主机开放端口；
- 不处理报告和业务配置。

## 4. 数据目录

App 数据卷布局固定为：

```text
/data/
├─ db/
│  └─ sub2api-report.db
├─ keys/                  # ASP.NET Core Data Protection key ring
├─ reports/
│  └─ YYYY/MM/<run-id>/
│     ├─ snapshot.json
│     └─ report.csv
├─ backups/
│  ├─ scheduled/
│  ├─ manual/
│  └─ updates/
├─ temp/
└─ instance-id
```

报告和 snapshot 可能包含人员用量，属于私有运行数据，永远不能复制到公开 GitHub Issue、测试 fixture 或仓库目录。

Updater 状态卷：

```text
/update-state/
├─ status.json
├─ operations/
├─ cache/
├─ downloads/
├─ backups/
└─ operations.lock
```

## 5. 启动配置与动态配置

`.env.example` 只包含容器启动前必须确定的端口、安装实例 ID 和 Docker Socket 补充组 GID；后两项由安装脚本生成：

```dotenv
APP_PORT=8080
BIND_ADDRESS=0.0.0.0
INSTANCE_ID=
DOCKER_GID=
```

Compose 内部固定数据库路径、运行环境、Updater 地址和 token 文件路径。这些值属于启动闭环，不提供业务页面修改。

时区、Release 通道、日志级别、报告保留月数和备份保留数量保存在 `SystemSettings`，通过页面修改并在运行期生效。Sub2API、SMTP、钉钉和飞书配置同样不写入 `.env`，而是在初始化后通过页面录入；其中凭证加密保存。Sub2API Admin API Key 使用持久化 Data Protection key ring 加密，读取接口只返回掩码；修改密钥要求 step-up。完整边界见 [配置管理策略](configuration.md)。

Updater 内部 token 由安装脚本生成到：

```text
./secrets/updater-token
```

权限设置为当前管理员可读，不能提交 Git。

## 6. 容器安全设置

App：

- 使用 .NET 非 root 用户；
- `read_only: true`；
- `/tmp` 使用 `tmpfs`；
- 只允许 `/data` 写入；
- `cap_drop: [ALL]`；
- `security_opt: [no-new-privileges:true]`；
- 设置 CPU/内存/PID 合理上限；
- 不使用 privileged；
- 不挂载宿主机目录或 Docker Socket。

Updater：

- 只挂载 Docker Socket、App 数据卷、Updater 状态卷、token 和只读发布公钥；
- 保持 non-root，通过安装脚本读取的 Socket GID 加入 supplemental group；
- 不使用 host network；
- 不映射端口；
- 不挂载宿主机根目录；
- 代码层只允许管理带当前 instance ID 标签的 App 容器；
- 只接受固定 GitHub 仓库 Release 路径、有效签名和匹配 SHA-256 的镜像归档；
- 通过 Docker Engine API 加载后校验镜像 ID、版本标签和 `linux/amd64`；
- 所有 Docker 变更写结构化审计日志。

Docker Socket 本身仍是 root 等价权限，不能通过 `cap_drop` 消除。安全边界主要依赖 updater 的最小接口、不可达性和操作 allowlist。

详细威胁、权限和故障验收见 [Updater 威胁模型](updater-threat-model.md)。

## 7. 网络与 HTTPS

应用容器只提供 HTTP `8080`，生产环境建议通过 Caddy、Traefik、Nginx 或云负载均衡终止 HTTPS。

推荐：

```text
Internet/Intranet
  -> HTTPS reverse proxy
  -> app:8080
```

要求反向代理：

- 正确传递 `X-Forwarded-Proto`；应用默认只处理一跳该 header，以便签发 Secure Cookie；
- 客户端地址默认不从转发 header 读取，避免未配置可信代理时伪造限流分区；
- 限制请求体大小；
- 配置 TLS 1.2+；
- 不缓存认证 API；
- 对登录和初始化保留应用返回的限流状态；
- 不在 access log 中记录 query secret。

直接暴露 `8080` 只适合受信内网测试。首次初始化码不能代替 HTTPS。

## 8. 首次启动

启动顺序：

1. Compose 创建数据卷、网络和两个服务。
2. App entrypoint 执行 Migrator。
3. App 加载或创建 Data Protection key ring。
4. App 检查管理员是否存在。
5. 无管理员时生成一次性初始化码并写 Docker 日志。
6. App readiness 成功，初始化页可访问。
7. 管理员创建完成后，初始化接口关闭。
8. 管理员登录并在配置清单中录入业务凭证。

安装脚本不能生成默认管理员密码，也不能把初始化码写入公开终端历史命令。

### 8.1 管理员密码恢复

在部署主机生成 15 分钟有效的一次性恢复码：

```bash
docker compose --env-file .env -f compose.yaml exec app appctl admin create-reset-code
```

恢复码只输出到当前终端并以哈希形式保存到 SQLite；再次生成会使旧码失效。管理员在 `/recover` 输入用户名、恢复码和新密码。连续失败会触发短时锁定，成功后恢复码立即失效并更新 Identity security stamp。

不要把恢复码写入 `.env`、Compose、Issue 或聊天记录。该流程不会发送邮件，也不会开放无需主机权限的“忘记密码”。

## 9. 健康检查

Compose healthcheck 使用：

```text
GET http://127.0.0.1:8080/health/live
```

Docker 健康状态只表示进程存活。反向代理和 updater 使用 `/health/ready` 判断是否可以接收业务请求。

建议参数：

```yaml
interval: 15s
timeout: 3s
retries: 4
start_period: 30s
```

SQLite migration 或升级维护阶段 readiness 失败是预期行为，不能据此无限重启容器。

## 10. 备份

### 10.1 升级备份

应用内升级和主机 `update.sh` 都会在替换 App 前创建数据库备份、执行 SQLite integrity check 并记录 SHA-256。应用内备份保存在 `updater-state`，手工部署契约更新备份保存在安装目录 `data-backups/`。

自动周备份、远端备份上传和备份管理页面不属于 1.0。

### 10.2 主机完整备份

主机完整备份包含 SQLite、Data Protection keys 和生成的报告。为保证一致性，先停止 App，再使用已经加载的 App 镜像创建 tar：

```bash
cd /opt/sub2api-report
sudo install -d -m 0700 backups
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
sudo docker compose stop app
sudo docker compose run --rm --no-deps --user 0:0 \
  --volume "$PWD/backups:/host-backup" \
  --entrypoint sh app -c \
  "tar -C /data -cf /host-backup/app-data-$timestamp.tar ."
sudo docker compose up -d --no-build app
sudo sha256sum "backups/app-data-$timestamp.tar" | sudo tee "backups/app-data-$timestamp.tar.sha256"
```

备份目录权限保持 `0700`，备份和 checksum 不得上传到公开 Issue、Actions artifact 或源码仓库。

### 10.3 主机恢复

恢复会覆盖当前数据，必须在主机控制台操作：

```bash
cd /opt/sub2api-report
backup=backups/app-data-YYYYMMDDTHHMMSSZ.tar
sudo sha256sum -c "$backup.sha256"
sudo docker compose stop app updater
sudo docker compose run --rm --no-deps --user 0:0 \
  --cap-add DAC_OVERRIDE --cap-add FOWNER --cap-add CHOWN \
  --volume "$PWD/backups:/host-backup:ro" \
  --entrypoint sh app -c \
  "rm -rf /data/* && tar -C /data -xf /host-backup/$(basename "$backup")"
sudo docker compose up -d --no-build
sudo docker compose ps
```

恢复后必须等待 App 和 Updater 均为 healthy，并登录检查系统设置和报告。恢复失败时保留原备份和 checksum，不继续删除其他文件。

## 11. 在线升级

页面一键升级遵循 [online-update.md](online-update.md)。

正常升级只造成迁移和容器切换期间的短暂不可用。升级前完成中的报表任务不会被强杀；若等待超时，升级取消而不是中断发送。

跨 deployment contract 的版本不支持页面升级，Release 页面提供新的 deploy bundle 和明确的主机命令。

## 12. 日志

默认只写 stdout/stderr，由 Docker logging driver 管理。应用不默认在 `/data` 写无限增长日志。

日志必须脱敏：

- 不记录 Cookie、Authorization、Admin API Key；
- 不记录 webhook URL 和 secret；
- 不记录 SMTP 密码；
- 不记录报告正文、真实人员清单和 CSV 内容；
- URL 只保留 scheme、host 和安全路径摘要；
- 错误响应正文经过 allowlist 后才记录。

生产建议配置 Docker 日志轮转：

```yaml
logging:
  driver: json-file
  options:
    max-size: 10m
    max-file: "5"
```

## 13. GitHub 仓库和 Release 制品

公开仓库包含：

- 源代码和合成 fixture；
- 架构、部署、开发和安全文档；
- `.env.example`；
- 生产 Compose 与本地开发 Compose override；
- PR/Main 质量工作流和 Release 构建工作流。

每个 GitHub Release 使用 Release Assets 发布：

```text
sub2api-report-v1.2.0-linux-amd64.tar.gz
sub2api-report-app-v1.2.0-linux-amd64.tar.gz
sub2api-report-updater-v1.2.0-linux-amd64.tar.gz
release-manifest.json
release-manifest.sig
checksums.txt
CHANGELOG.md
LICENSE
release-notes-v1.2.0.md
sub2api-report-app-v1.2.0.spdx.json
sub2api-report-updater-v1.2.0.spdx.json
```

完整 bundle 包含生产 Compose、脚本、Apache-2.0 许可证、完整变更日志、当前版本说明和两个离线镜像归档。GitHub Release 页面正文与 `release-notes-vX.Y.Z.md` 都由根目录 `CHANGELOG.md` 的对应版本章节生成；版本章节缺失或为空时发布失败。普通在线升级只下载 App 镜像归档；Updater 或部署契约变化要求下载新的完整 bundle 并执行 `update.sh`。

生产 Compose 固定引用本地标签：

```text
sub2api-report-app:current
sub2api-report-updater:bootstrap
```

两个服务都设置 `pull_policy: never`。这些本地标签只用于选择已校验并通过 `docker load` 导入的镜像；发布信任由 manifest 签名、归档 SHA-256、预期镜像 ID 和架构共同建立。GitHub Actions artifact 只用于 Job 间传递，最终安装文件必须进入无保留期依赖的 GitHub Release Assets。

Release workflow 需要仓库 Actions secret `RELEASE_SIGNING_KEY_PEM`，内容为专用 RSA 私钥 PEM。私钥不能提交到仓库，也不能提供给 PR Job；工作流只在 Tag 发布 Job 的临时目录中使用它，并从同一密钥导出 bundle 内公钥。密钥轮换必须单独发布公告，现有安装默认拒绝未确认的公钥变化。Tag 版本必须与 `Directory.Build.props` 的 `VersionPrefix` 和 `CHANGELOG.md` 对应版本章节一致；该章节被提取为 Release 页面正文并以独立资产发布，其 SHA-256 和大小写入签名 manifest。工作流完成质量门和扫描后创建 draft Release，由维护者审核后发布。

## 14. 仓库隐私保护

必须在实现第一阶段建立 `.gitignore`：

```gitignore
.env
.env.*
!.env.example
secrets/
*.db
*.db-shm
*.db-wal
data/
reports/
backups/
logs/
test-results/
*.har
*.trace.zip
```

CI 增加：

- GitHub secret scanning；
- Gitleaks 或等价扫描；
- 对测试 fixture 中邮箱、域名、Key 格式做检查；
- 对 Docker build context 做敏感文件检查；
- 发布前检查工作树不含数据库、报告、日志和截图。

所有示例使用 `example.com`、`用户 A` 和假 ID。具体规则见 [public-repository-policy.md](public-repository-policy.md)。

## 15. 资源建议

最小配置：

```text
CPU: 1 core
RAM: 512 MiB
Disk: 1 GiB + retained reports/backups
```

建议配置：

```text
CPU: 2 cores
RAM: 1 GiB
Disk: 5 GiB or more
```

镜像拉取和在线升级需要额外空间同时保留当前与目标镜像。Preflight 应按目标镜像大小、数据库和备份大小估算空间。

## 16. 支持边界

首版支持：

- Linux amd64；
- Docker Engine 当前维护版本；
- Docker Compose v2；
- named volume；
- 单实例；
- 标准反向代理。

首版不承诺：

- Windows container；
- arm64；
- Podman；
- rootless Docker；
- Docker Swarm；
- Kubernetes；
- NFS 上的 SQLite；
- 多 App 副本共享一个 SQLite。

## 17. 部署验收

- 全新主机执行安装脚本后两个服务健康。
- 日志出现一次性初始化码且初始化后不再出现。
- 数据卷删除前，容器重建不丢管理员、配置和报告。
- App 容器无 Docker Socket、非 root、只写 `/data`。
- Updater 无主机端口，只操作当前项目标签资源。
- 日志和公开仓库不含真实身份、凭证或报告内容。
- 备份通过 integrity check，并能在测试副本恢复。
- 从前一稳定版本页面升级成功。
- 注入 migration 失败后旧版本和旧数据库自动恢复。
