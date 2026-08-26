# Sub2API Codex API Key 用量月报技术调查

调查日期：2026-08-26

本文只记录数据源与报告渠道调查。最终工程方案以 [系统架构](architecture.md)、[Docker 部署](deployment.md) 和 [在线升级](online-update.md) 为准。

## 1. 结论

可以直接通过 Sub2API API 构建报表系统，不需要自动操作 Sub2API 网页，也不需要依赖网页导出的 CSV。

推荐方案：

1. 每月 1 日 09:00（`Asia/Shanghai`）由系统内置计划任务生成报告。
2. 使用 Sub2API Admin API Key 调用管理 API。
3. 通过稳定的 `api_key_id` 将一个或多个 Key 映射到人员。
4. 分别统计截至上月最后一天的最近 7 个完整自然日和最近 30 个完整自然日。
5. 生成统一报表模型，再按配置组合发送到邮箱、钉钉群机器人和飞书群机器人。
6. 保存本次报表快照和分渠道发送结果，避免重复发送并方便审计。

当前无需改 Sub2API。本期数据量如果只是几十个 Key，每月约 `2 * Key 数量` 次统计请求，开销可以忽略。只有 Key 达到数百个或后续改为高频日报时，才值得给 Sub2API 增加“一次按 Key 分组聚合”的专用接口。

## 2. 已确认的 Sub2API 能力

本次核对上游仓库 `Wei-Shaw/sub2api` 的 `efb46db0a960fdad94502b1c3a982a0051cf5245` 版本源码。

### 2.1 可按 API Key 和日期统计

用户接口：

```http
GET /api/v1/usage/stats
  ?api_key_id=123
  &start_date=2026-07-02
  &end_date=2026-07-31
  &timezone=Asia/Shanghai
Authorization: Bearer <user-jwt>
```

管理接口：

```http
GET /api/v1/admin/usage/stats
  ?api_key_id=123
  &start_date=2026-07-02
  &end_date=2026-07-31
  &timezone=Asia/Shanghai
  &nocache=true
x-api-key: <admin-api-key>
```

返回的核心字段包括：

- `total_requests`
- `total_input_tokens`
- `total_output_tokens`
- `total_cache_creation_tokens`
- `total_cache_read_tokens`
- `total_tokens`
- `total_cost`
- `total_actual_cost`
- `average_duration_ms`

`total_cost` 是标准计费金额；`total_actual_cost` 是应用分组等倍率后的实际用户计费金额。月报建议同时保留两者，以 `total_actual_cost` 作为默认费用列。

接口的日期语义是闭合的自然日参数，服务端实际转换为半开区间 `[start_date 00:00, end_date + 1 day 00:00)`，并支持指定时区。

### 2.2 可以获取 Key ID 和名称

推荐使用管理接口分页获取目标用户的 Key：

```http
GET /api/v1/admin/users/{user_id}/api-keys?page=1&page_size=100
x-api-key: <admin-api-key>
```

响应中有 `id`、`name`、`status`、`last_used_at`、`group_id` 等字段。

用户侧也有 `GET /api/v1/keys`，但它要求用户 JWT，不适合作为长期无人值守的机器认证。

### 2.3 Admin API Key 适合定时任务

Sub2API 管理 API 原生支持：

```http
x-api-key: <admin-api-key>
```

相比登录后保存 JWT，这种方式没有 access token 刷新和 2FA 登录问题。权限很高，因此必须只放在 Secret/环境变量中，并限制报表容器和网络访问范围。

如果安全要求不允许报表程序持有全局 Admin API Key，次选方案是给 Sub2API 增加一个只读 reporting token；不建议定时模拟账号登录。

### 2.4 网页“导出完整记录”的实际实现

当前上游前端的 CSV 导出不是独立的服务端导出 API。浏览器会按每页 100 条循环调用 `/usage`，拉取所有明细后在前端拼 CSV。

因此不建议自动化点击网页导出：

- 明细数量大时请求多、速度慢；
- 浏览器自动化容易受登录、页面改版和超时影响；
- 报表只需要聚合数据，直接调用 `/usage/stats` 更准确、开销更小。

### 2.5 另一个可用但非首选的接口

新版本有：

```http
GET /api/v1/user/api-keys/{id}/usage/daily?days=30
```

`days` 支持 1 到 90，返回按天的请求、各类 Token 和费用。它适合 UI 展示，但要求用户 JWT，并且默认包含运行当天的部分数据。月报使用管理统计接口传明确日期更稳妥。

## 3. 统计口径

### 3.1 推荐窗口

月报在每月 1 日运行，统计运行日之前的完整自然日，不包含 1 日当天的部分数据。

例如报告日为 2026-08-01：

| 窗口 | 开始日期 | 结束日期 | 天数 |
| --- | --- | --- | ---: |
| 最近 7 天 | 2026-07-25 | 2026-07-31 | 7 |
| 最近 30 天 | 2026-07-02 | 2026-07-31 | 30 |

这不是“上一个自然月”。如果业务最终要核算 7 月整月，应另加 `previous_calendar_month` 口径；当前需求明确只要 7 天和 30 天，因此先不增加。

### 3.2 Codex 范围

需要先确认 Sub2API 中该账号/分组是否只承载 Codex：

- 如果目标用户和 Key 只访问 Codex，按 `api_key_id` 统计即可。
- 如果同一个 Key 还能访问 Claude、Gemini 等平台，应额外传 Codex 的 `group_id`。
- 不建议只靠单个 `model` 参数过滤，因为人员可能使用多个 Codex 模型。

### 3.3 Key 到人员的映射

不要把可修改的 Key 名称当作唯一标识。建议维护显式配置，以稳定的数据库 ID 为主键，并允许一人对应多个 Key：

```yaml
people:
  - id: user-a
    name: 用户 A
    key_ids: [101, 118]
  - id: user-b
    name: 用户 B
    key_ids: [102]
```

聚合规则：

- 一个人有多个 Key 时先按 Key 拉取，再汇总到人员。
- 报表保留人员汇总，并可附 Key 明细用于核对。
- 自动发现没有映射的新 Key 并报警，不能静默遗漏。
- 删除或轮换 Key 后，旧 `api_key_id` 仍保留在映射中至少 30 天，避免历史窗口漏算。
- 报表中不得展示完整 Key 明文。

上游当前删除 Key 的实现是软删除并替换凭证明文，因此历史 ID 可以继续作为统计标识；但当前 Key 列表通常不会展示已删除项，显式映射仍然必要。

### 3.4 推荐报表列

人员汇总表：

- 人员
- Key 名称或 Key 数量
- 7 天请求数、输入 Token、输出 Token、缓存 Token、总 Token、实际费用
- 30 天请求数、输入 Token、输出 Token、缓存 Token、总 Token、实际费用
- 30 天日均费用
- 最后使用时间

同时增加：

- 全员合计
- 数据起止日期和时区
- 未映射 Key、接口失败 Key、零用量 Key 提示
- 生成时间和唯一报告 ID

## 4. 推荐架构

采用 `.NET 10 + React/Vite/shadcn/ui + SQLite` 模块化单体：

```text
ASP.NET Core App
  +-- React SPA
  +-- Sub2API client
  +-- report engine
  +-- Quartz scheduler
  +-- email / DingTalk / Feishu senders
  +-- SQLite snapshots and delivery records
```

前后端由同一个 App 容器提供，Quartz 在应用内持久化调度。Docker Compose 另带一个不暴露端口的 updater sidecar，用于页面一键升级和失败回滚；App 本身不挂载 Docker Socket。

详细模块、数据模型和安全边界见 [系统架构](architecture.md)。

## 5. 发送渠道

三个渠道使用同一个报表数据模型，但分别渲染，不要试图复用完全相同的 Markdown。

### 5.1 邮箱

- SMTP over TLS/STARTTLS。
- 收件人通过配置管理，支持多个收件人和抄送。
- 正文使用 HTML 汇总表。
- 附件使用 UTF-8 BOM CSV，便于 Excel 正确显示中文。
- 主题示例：`[Codex 用量月报] 2026-07-02 至 2026-07-31`。
- 凭证只从 Secret/环境变量读取。

### 5.2 钉钉

- 使用群自定义机器人 webhook。
- 启用加签，程序实现 HMAC-SHA256 签名。
- 使用 Markdown 消息，但只使用官方支持的子集；人员数据用分行列表，避免依赖 Markdown 表格兼容性。
- 校验 HTTP 状态码和响应 JSON 错误码。
- 官方限制为每个机器人每分钟最多 20 条，超限可能被限流 10 分钟。本报表正常只需 1 到数条。

### 5.3 飞书

- 使用群自定义机器人 webhook。
- 启用签名校验，签名和 webhook secret 从 Secret 注入。
- 使用 `post` 富文本消息；内容过长时分片，并在每片标注 `1/N`。
- 校验 HTTP 状态码和响应 JSON 错误码。
- 官方文档给出的自定义机器人限制是每个租户、每个机器人 100 次/分钟且 5 次/秒，请求体不超过 20 KB。

### 5.4 组合发送与失败策略

配置示意：

```yaml
channels:
  email:
    enabled: true
    to: [recipient-a@example.com, recipient-b@example.com]
  dingtalk:
    enabled: true
  feishu:
    enabled: false
```

发送必须相互隔离：

- 邮件失败时继续发送钉钉和飞书。
- 网络和 `429/5xx` 做有限次数指数退避。
- 鉴权、签名、请求格式等 `4xx` 不盲目重试。
- 最终只要有一个已启用渠道失败，任务退出非零并发出运维告警。
- 重跑时只补发失败渠道，已成功渠道不重复发送。

## 6. 调度、幂等和留档

### 6.1 调度

应用使用 Quartz.NET 持久化月报计划，默认每月 1 日 09:00 按 `Asia/Shanghai` 运行。管理员可以在页面停用计划、修改时间、手工 dry-run 和补跑。

调度位于独立报表系统中，不修改 Sub2API 进程。当前产品限定单实例，Quartz 禁止同一报告并发执行，数据库唯一键继续提供最终幂等保证。

### 6.2 幂等

任务调度不是严格 exactly-once。建议报告 ID 使用：

```text
codex-usage:{report_date}:{timezone}
```

发送记录至少包含：

```text
report_id, channel, payload_hash, status, attempts, sent_at, error
```

幂等状态写入应用 SQLite，并随 `/data` Docker volume 持久化。

### 6.3 留档

每次保留：

- 原始 API 响应 JSON（脱敏）
- 聚合后的规范 JSON
- 最终 CSV
- 各渠道发送结果

建议至少保留 12 个月，方便核对历史报告；这也能隔离 Sub2API 后续清理原始明细带来的影响。

## 7. 风险和约束

| 风险 | 影响 | 处理建议 |
| --- | --- | --- |
| 部署版本较旧 | 部分接口或字段不存在 | PoC 先做接口探测；最低只依赖较基础的 `/admin/usage/stats` |
| 原始用量保留不足 30 天 | 30 天统计不完整 | 检查 `dashboard_aggregation.retention.usage_logs_days >= 31`；上游示例默认 90 天 |
| Key 名称被修改或 Key 被轮换 | 人员归属错误或漏算 | 使用 `api_key_id` 显式映射，旧 ID 延迟移除 |
| 一个 Key 被多人共享 | 无法可靠按人拆分 | 管理制度上保证一人一 Key；共享 Key 只能报告为“共享” |
| 一人使用多个 Key | 人员用量被拆散 | 配置允许 `key_ids` 数组并按人员汇总 |
| 同一 Key 可访问多个平台 | 报表混入非 Codex 用量 | 使用 Codex `group_id` 过滤 |
| Admin API Key 泄露 | 可访问高权限管理 API | Secret 管理、最小网络权限、日志脱敏；长期考虑只读 reporting token |
| 统计中途有迟到记录 | 两次运行结果略有差异 | 每月 1 日 09:00 运行；快照留档；必要时提供人工补跑 |
| webhook 返回 HTTP 200 但业务失败 | 误判发送成功 | 同时检查响应 JSON 的业务错误码 |
| 人员过多导致消息过长 | webhook 发送失败或难阅读 | 邮件附完整 CSV；IM 消息分片或只发摘要 |

## 8. 推荐实施阶段

### 阶段一：只读 PoC

1. 确认部署版本和 API 基地址。
2. 创建或获取 Admin API Key。
3. 确认目标 `user_id`、Codex `group_id` 和全部人员-Key 映射。
4. 对 1 到 2 个 Key 调 7 天、30 天统计，并与 Sub2API 页面手工核对。
5. 确认 `total_actual_cost` 是否是业务希望展示的“用量/费用”口径。

### 阶段二：最小可用版本

1. 实现 Sub2API client、日期窗口和人员汇总。
2. 输出规范 JSON 和 CSV。
3. 实现邮箱、钉钉、飞书 Sender，可组合启用。
4. 加入重试、幂等、脱敏日志和失败退出码。
5. 用手工命令 dry-run，确认收件人和消息格式。

### 阶段三：上线

1. 发布 App 和 Updater amd64 镜像及 Docker Compose 安装包。
2. 启用 Quartz 月报计划，默认每月 1 日 09:00 `Asia/Shanghai`。
3. 首月并行人工核对。
4. 增加任务失败告警、12 个月快照留档和在线升级回滚验证。

## 9. 开发前待确认信息

必须确认：

- Sub2API 基地址和当前版本/commit。
- 目标 Sub2API `user_id`。
- Codex 是否独占该用户/Key；否则提供 Codex `group_id`。
- 全量 `api_key_id -> 人员` 映射，以及是否存在共享 Key/一人多 Key。
- 报表的核心指标是请求数、Token、标准费用、实际费用中的哪些；建议默认全部保留。
- 邮件地址、SMTP 服务和发件人。
- 钉钉 webhook + secret、飞书 webhook + secret。
- 实际启用的渠道组合。
- 部署环境是 Kubernetes 还是单机 Docker。
- 报告发送时间是否确定为每月 1 日 09:00，时区是否为 `Asia/Shanghai`。

## 10. 参考资料

Sub2API：

- [用户用量接口源码](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/backend/internal/handler/usage_handler.go)
- [管理员用量接口源码](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/backend/internal/handler/admin/usage_handler.go)
- [用户 API Key 接口源码](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/backend/internal/handler/api_key_handler.go)
- [Admin API Key 认证源码](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/backend/internal/server/middleware/admin_auth.go)
- [前端用量 API 封装](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/frontend/src/api/usage.ts)
- [前端 CSV 导出实现](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/frontend/src/views/user/UsageView.vue)
- [默认数据保留配置](https://github.com/Wei-Shaw/sub2api/blob/efb46db0a960fdad94502b1c3a982a0051cf5245/deploy/config.example.yaml)

发送与调度：

- [钉钉自定义机器人发送群消息](https://open.dingtalk.com/document/development/custom-robots-send-group-messages)
- [钉钉机器人安全设置](https://open.dingtalk.com/document/dingstart/customize-robot-security-settings)
- [飞书自定义机器人指南](https://open.feishu.cn/document/client-docs/bot-v3/add-custom-bot?lang=zh-CN)
- [项目系统架构](architecture.md)
- [Docker 部署方案](deployment.md)
- [在线升级方案](online-update.md)
