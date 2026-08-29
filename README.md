# Sub2API Report

Sub2API Report 是一个单管理员的 Codex API Key 用量报告系统。它从 Sub2API 自动同步用户和 API Key，按用户 → Key 汇总用量，保存报告快照，并定时发送报告。

## 主要功能

- 支持滚动 7/30 天、上一自然周、上一自然月和自定义日期范围；
- 支持不可变报告快照和 UTF-8 BOM CSV；
- 支持邮件、钉钉、飞书组合投递和失败补发；
- 支持 Quartz 持久化月报计划、任务重试和重启恢复；
- 支持签名 Release、页面在线更新和失败自动回滚；
- 使用 SQLite 存储数据，无需单独部署数据库。

## 部署要求

- Linux `amd64`；
- Docker Engine；
- Docker Compose v2。

不支持 rootless Docker、Podman、Swarm、Kubernetes 和 arm64。

## 方式一：服务器一键部署

脚本会自动下载并校验最新 Release、加载镜像、生成配置并启动服务：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh | sudo bash
```

安装目录：

```text
/opt/sub2api-report
```

默认访问地址：

```text
http://<服务器地址>:8080
```

查看日志：

```bash
cd /opt/sub2api-report
sudo docker compose logs -f app
```

以后再次执行同一条一键部署命令即可更新到最新版本。

## 方式二：Docker Compose 部署

先准备最新 Release、镜像和 Compose 文件，但不启动容器：

```bash
curl -fsSL https://raw.githubusercontent.com/lohaaa/sub2api-report/main/deploy/bootstrap.sh |
  sudo SUB2API_REPORT_START=false bash
```

然后手工管理容器：

```bash
cd /opt/sub2api-report
sudo docker compose up -d
sudo docker compose ps
sudo docker compose logs -f app
```

停止服务：

```bash
cd /opt/sub2api-report
sudo docker compose down
```

## 首次初始化

首次启动后，从 App 日志中读取一次性管理员初始化码：

```bash
cd /opt/sub2api-report
sudo docker compose logs app
```

打开 `http://<服务器地址>:8080`，使用初始化码创建管理员，然后配置 Sub2API、报告渠道和月报计划。

更多配置、备份、恢复和升级说明见 [部署文档](docs/deployment.md)。

## License

Sub2API Report 使用 [Apache License 2.0](LICENSE)。
