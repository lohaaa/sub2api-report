# Sub2API Report

Sub2API Report 是一个单管理员的 Codex API Key 用量报告系统。它从 Sub2API 自动同步用户和 API Key，按用户 → Key 汇总用量，保存报告快照，并定时发送报告。

## 主要功能

- 支持滚动 7/30 天、上一自然周、上一自然月和自定义日期范围；
- 支持不可变报告快照和多工作表 XLSX 导出；
- 支持邮件、钉钉、飞书组合投递和失败补发；
- 支持 Quartz 持久化月报计划（每月 1–31 日，短月取月末或跳过）、任务重试和重启恢复；
- 支持签名 Release、更新备份和失败回滚；
- 使用 SQLite 存储数据，无需单独部署数据库。

正式发行版支持 Linux `amd64`，提供服务器直接部署和 Docker Compose 两种方案。

## 方式一：服务器直接部署

适用于使用 systemd 的 Linux 服务器，**不需要 Docker，也不需要预装 .NET Runtime**。

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/server-bootstrap.sh | bash
```

Report 端口可独立指定，默认 `8081`。例如改为 `18080`：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/server-bootstrap.sh | \
  SUB2API_REPORT_PORT=18080 bash
```

更新现有安装时不传该变量会保留当前端口。

脚本以当前用户下载并校验 self-contained 服务器程序，仅在安装依赖、写入系统目录和注册 systemd 服务时调用 `sudo`。

默认访问地址：

```text
http://<服务器地址>:8081
```

与已有 Sub2API 共机部署时，可在初始化后将 Sub2API 地址设为 `http://127.0.0.1:8080`。

常用命令：

```bash
sudo systemctl status sub2api-report
sudo journalctl -u sub2api-report -f
sudo systemctl restart sub2api-report
```

数据目录：

```text
/var/lib/sub2api-report
```

以后再次执行同一条安装命令即可更新到最新版本；更新失败时会恢复旧程序和更新前数据库。

## 方式二：Docker Compose 部署

服务器需要提前安装 Docker Engine 和 Docker Compose v2。首次安装和以后更新到最新正式版都执行同一条命令：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | bash
```

脚本会下载并校验签名完整 bundle，默认安装到 `/opt/sub2api-report`、监听 `0.0.0.0:8081` 并启动服务。更新时会保留现有端口、监听地址、实例 ID、内部 token 和数据卷；替换前备份 SQLite，验证失败时恢复旧镜像、配置和数据库。

常用可选参数：

| 参数 | 默认值 | 作用 |
| --- | --- | --- |
| `SUB2API_REPORT_VERSION` | `latest` | 固定安装指定正式版本，例如 `1.1.3` |
| `SUB2API_REPORT_INSTALL_DIR` | `/opt/sub2api-report` | 安装 Compose、`.env` 和发布元数据的目录 |
| `SUB2API_REPORT_PORT` | 新安装为 `8081` | 设置 Report 主机端口；更新时省略则保留现有端口 |
| `SUB2API_REPORT_BIND_ADDRESS` | 新安装为 `0.0.0.0` | 设置监听地址；更新时省略则保留现有地址 |
| `SUB2API_REPORT_START` | `true` | 设为 `false` 时完成安装/更新验证后保持容器停止 |

例如仅允许本机访问并使用端口 `18080`：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | \
  SUB2API_REPORT_PORT=18080 \
  SUB2API_REPORT_BIND_ADDRESS=127.0.0.1 \
  bash
```

日常检查和停止：

```bash
cd /opt/sub2api-report
sudo docker compose ps
sudo docker compose logs -f app
sudo docker compose down
```

管理页面只会为兼容文件明确验证过的源版本显示“安装更新”。页面提示“需要主机升级”时，重新执行上面的无参数 bootstrap 命令即可完成 App、Updater 和部署文件的完整更新。

## 首次初始化

首次启动后读取一次性管理员初始化码。

服务器直接部署：

```bash
sudo journalctl -u sub2api-report -b --no-pager -o cat | \
  grep -F "One-time setup code"
```

Docker Compose 部署：

```bash
cd /opt/sub2api-report
sudo docker compose logs --no-log-prefix app 2>&1 | \
  grep -F "One-time setup code"
```

输出中 `One-time setup code:` 后的值即为初始化码，默认有效期 30 分钟。若尚未输出，确认服务已启动后重试对应命令。

打开 `http://<服务器地址>:8081`，使用初始化码创建管理员，然后配置 Sub2API、报告渠道和月报计划。

更多配置、备份和恢复说明见 [服务器部署文档](docs/server-deployment.md) 或 [Docker 部署文档](docs/deployment.md)。

## License

Sub2API Report 使用 [Apache License 2.0](LICENSE)。
