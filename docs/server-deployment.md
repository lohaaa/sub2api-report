# 服务器直接部署

- 平台：使用 systemd 的 Linux amd64
- 运行方式：self-contained ASP.NET Core 服务
- Docker：不需要
- .NET Runtime：不需要

## 安装和更新

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/server-bootstrap.sh | sudo bash
```

bootstrap 自动解析最新 GitHub Release，下载 `sub2api-report-server-vX.Y.Z-linux-amd64.tar.gz` 和 checksums，校验后调用包内 `server-install.sh`。

安装器会：

- 创建系统用户 `sub2api-report`；
- 安装版本到 `/opt/sub2api-report-server/releases/<version>`；
- 使用 `/opt/sub2api-report-server/current` 原子切换当前版本；
- 使用 `/var/lib/sub2api-report` 保存 SQLite、Data Protection keys 和报告；
- 使用 `/etc/sub2api-report/environment` 保存监听地址和启动闭环配置；
- 安装并启动 `sub2api-report.service`；
- 等待 `/health/ready` 成功；
- 更新失败时恢复旧版本和更新前数据库。

## 服务管理

```bash
sudo systemctl status sub2api-report
sudo systemctl restart sub2api-report
sudo systemctl stop sub2api-report
sudo journalctl -u sub2api-report -f
```

修改监听地址或端口：

```bash
sudo editor /etc/sub2api-report/environment
sudo systemctl daemon-reload
sudo systemctl restart sub2api-report
```

默认监听：

```text
http://0.0.0.0:8080
```

生产环境建议在前方使用 Caddy、Nginx 或云负载均衡提供 HTTPS。

## 初始化和恢复管理员

首次启动后从日志读取一次性初始化码：

```bash
sudo journalctl -u sub2api-report
```

生成管理员密码恢复码：

```bash
sudo sub2api-reportctl admin create-reset-code
```

## 数据和备份

```text
/var/lib/sub2api-report/
├─ db/
├─ keys/
├─ reports/
└─ temp/
```

更新前数据库备份保存在：

```text
/var/backups/sub2api-report/
```

主机完整备份应至少包含 `/var/lib/sub2api-report` 和 `/etc/sub2api-report`。备份和日志不得上传到公开仓库或 Issue。

## 更新方式

再次执行安装命令即可更新到最新版本：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/server-bootstrap.sh | sudo bash
```

服务器直接部署不运行 Docker Updater，管理页面的 App-only Docker 在线更新不可用；更新由 systemd bootstrap 完成，并包含停服、数据库备份、健康检查和失败回滚。
