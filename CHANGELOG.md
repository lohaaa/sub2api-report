# Changelog

本项目的所有重要变更都记录在此文件中。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

首个公开版本为 `v1.0.0`；此前的 `0.x` 仅作为内部开发里程碑，未创建 GitHub Release。

## [Unreleased]

## [1.1.3] - 2026-09-01

### Fixed

- 修复自动月报在 cron 触发时因 trigger 不包含手工任务专用的 `reportRunId`，被 `JobDataMap.GetString` 抛出的 `KeyNotFoundException` 中断且不创建执行记录的问题。`ScheduledReportJob` 现在明确区分自动 trigger、手工/重试 trigger 和 recovery：自动 trigger 创建幂等计划执行记录；手工 trigger 复用已持久化 run；非法或不存在的手工 runId 明确失败，不再错误降级为自动任务。

### Upgrade

- v1.1.3 保持 manifest schema v3 和 deployment contract v1，但自动月报修复需要更新 App；兼容文件继续要求使用完整 bundle。Docker 部署再次执行 README 中的无参数 bootstrap 命令。

## [1.1.2] - 2026-08-31

### Added

- 新增版本化 `deploy/release-compatibility.json` 作为 Release 升级策略唯一权威文件；manifest schema v3 签名携带最低 Updater、手工/在线模式、精确 `onlineUpgradeFrom` 源版本列表和升级提示。Shell、C#、bundle 验签、Updater 检查/安装门禁和前端统一消费同一策略；未列出的源 App、过旧 Updater 或不一致部署契约在下载 App 归档前转为完整 bundle 指引。
- App 维护握手新增非破坏性维护资格与阻断原因；存在活动或遗留报告任务时，Updater 在归档下载前终止，真正进入维护时继续二次检查。

### Changed

- Candidate 不再从当前源码重复构建“上一版”，改为下载、checksum 校验、同信任公钥验签真实上一公开 Release bundle，目标候选只构建一次；真实 Docker 场景覆盖宿主机 v1.0.8 metadata 落后、实际 App 已更新、`current` 标签污染、失败回滚、成功更新和同版本幂等同步。
- Docker README 统一首次安装与完整更新为同一条无参数 bootstrap 命令；新增版本、安装目录、端口、监听地址和启动行为参数表。`SUB2API_REPORT_PORT` 与 `SUB2API_REPORT_BIND_ADDRESS` 可直接传入，更新时省略会保留现有值。

### Fixed

- 完整 bundle 更新不再使用可能因历史 App-only 更新而滞后的宿主机 `release-manifest.json` 判断当前版本或选择回滚镜像；改为读取实际 App/Updater 容器、镜像版本和 upgrade-operation 标签。单容器、原始+候选双容器、历史成功在线升级容器都能确定正确基线。
- 同版本完整更新改为幂等同步 Compose、兼容文件和签名发布元数据，不重复备份数据库；`.env` 纳入更新配置备份，端口/监听地址修改失败时可随部署一起回滚。

### Upgrade

- v1.1.2 的兼容契约要求同时更新 App 与 Updater，`manualUpgradeRequired=true`、`onlineInstallSupported=false`。v1.1.1 及更早 Docker 部署请再次执行 README 中的无参数 bootstrap 命令。

## [1.1.1] - 2026-08-31

### Changed

- Release 构建默认使用 `manualUpgradeRequired=true`、`onlineInstallSupported=false`，并将 `minimumUpdaterVersion` 设为当前发布版本。只有经过旧 App 与旧 Updater 兼容测试的 App-only 版本，才允许显式开启在线安装并通过 `MINIMUM_UPDATER_VERSION` 复用旧 Updater，避免发布元数据静默放行不兼容链路。

### Fixed

- 修复在线升级维护失败时丢弃 App Problem Details 的问题，升级操作现在保留“存在活动报告任务”等受控原因；无效响应继续使用固定脱敏文案。
- 修复目标镜像在维护和备份前过早占用 `sub2api-report-app:current` 标签的问题；标签切换延后到已记录旧容器且可回滚的替换阶段。完整 bundle 更新从实际 App/Updater 容器读取旧 image ID，并在候选 App 残留时按已安装版本筛选旧 App，不再把受污染标签或候选镜像当作回滚基线。

### Upgrade

- 从 v1.0.8 升级到包含本修复的版本时不提供页面 App-only 安装，必须使用目标完整 bundle 的 `update.sh`。脚本停止旧 App 后创建独立数据库备份，再更新 App 与 Updater，并在健康验证失败时恢复旧镜像、配置和数据库。

## [1.1.0] - 2026-08-31

### Added

- 月报计划支持每月 1–31 日，并新增稳定的短月策略 `ShortMonthStrategy`：`UseLastDay`（默认，当月无该日期时在最后一天执行）与 `SkipMonth`（当月跳过）。Domain/Application/API/Infrastructure 全链路透传；API 请求中的策略字段为可选，旧前端省略时保留已存策略或使用默认值，不会返回 400。前端日期改为 Select（1–31），大于 28 日时提供"短月取月末"/"跳过该月"切换并说明示例行为，小于等于 28 日时隐藏控件但保留已存值。

### Changed

- Quartz 调度可靠执行短月策略：仅 `UseLastDay` 且日期大于 28 时额外创建一个月末 (`L`) 后备 trigger 与指定日 primary trigger 共用同一 durable job（FireAndProceed、时区、RequestRecovery 不变）；job 按 trigger key 与 `ScheduledFireTimeUtc` 判定执行，指定日恰为月末时仅 primary 执行一次。协调器 Apply 清理多余 trigger，投影 NextRunAt 取全部有效 trigger 最小值，同步验证包含数量/cron/时区/misfire/策略。
- 前端计划页"立即运行"降低视觉层级（outline），成功提示改为"已保存"（移除 revision 技术文案），同步错误显示友好文案而非裸 code，启用渠道计数改为指向 `/channels` 的链接；保存按钮位于全部可编辑字段之后。
- 升级操作进入终态后前端仅触发一次 `updates/status`、`updates/plan`、`system/version` 刷新（不中途 reload、不自动 reload）；`succeeded`/`rolled_back`/`failed` 后版本状态保持一致。
- CI 构建运行时从 Node.js 22 切换到 Node.js 24（LTS，Active LTS，2026-10 起为唯一非 EOL LTS），Dockerfile 前端构建镜像同步为 `node:24-alpine`。

### Fixed

- 修复报告任务在快照持久化后永久无法进入投递的问题（线上事故：任务永久停留在 Rendering、快照非空但投递记录为空）。根因是共享 DbContext 的跨阶段变更跟踪残留：对已跟踪（非 Added）的执行记录用集合导航添加新投递图时，EF Core 会把新的 `DeliveryRecord`/`DeliveryPart` 解析为 Modified，保存时执行 UPDATE 命中 0 行并抛出 `DbUpdateConcurrencyException`，任务执行器的失败收敛与审计写入还会重复冲刷同一污染批次。现在生成快照成功进入投递前清理阶段边界的 ChangeTracker 并让投递按 runId 重新加载执行记录；新投递与重试新增分片改为显式主外键加 `DbSet.Add` 追踪；失败收敛改为清理后按主键重读、未终态才落 Failed，对并发提供一次有限刷新重试，仍失败仅告警并由启动恢复（Queued/Collecting/Rendering 收敛为 Failed 且可重试，Delivering 保留给 Quartz 恢复）。新增真实 SQLite 集成测试覆盖 RunNow 全流程（2 个启用渠道→快照→投递→Succeeded）、并发 Revision 写入共存、投递阶段异常后终态可重试且快照关联保留，以及跟踪模式回归（集合导航解析为 Modified 会复现冲突、显式外键 DbSet.Add 状态为 Added 且落库成功）。
- 升级构建工具链：所有使用弃用 Node20 运行时的 GitHub Actions 升级到官方最新稳定主版本（Node24）：checkout v4→v7.0.1、setup-dotnet v4→v6.0.0、setup-node v4→v7.0.0、upload-artifact v4→v7.0.1、attest-build-provenance v2→v4.2.2、pnpm/action-setup v4→v6.0.10、docker/setup-buildx-action v3→v4.3.0、docker/build-push-action v6→v7.3.0、anchore/sbom-action v0.22.2→v0.24.2；全部继续 commit SHA pin。trivy-action v0.36.0 为 composite（不依赖 Node 运行时）保持不变。

### Migration

- 新增数据库迁移 `AddShortMonthStrategy`：`ReportSchedules` 增加 `ShortMonthStrategy` 列（默认 `UseLastDay`）、`DayOfMonth` check 改为 1–31、新增策略 check 约束；存量数据自动回填 `UseLastDay`。
## [1.0.8] - 2026-08-31

### Fixed

- 修复在线升级候选 App 容器创建失败：当部署同时声明宿主机 `Binds` 与有效挂载（官方 Compose 的 named volume 数据卷加只读 updater token bind）时，重建容器会携带重复挂载点并被 Docker Engine 以 duplicate mount point 拒绝，导致升级在 `replacing_app` 阶段失败并自动回滚。现在创建候选与回滚容器时保留 `Binds` 原字符串（含 `ro`/propagation 选项），仅重放未被 `Binds` 覆盖的有效挂载（named volume、仅通过 `--mount` 表达的 bind/tmpfs），快照冲突时拒绝发送创建请求；named volume 重放改用 volume 名称而非宿主机数据路径。新增映射测试覆盖官方 Compose 契约、`--mount` 挂载、短语法覆盖与持久化回滚快照。
- v1.0.8 起 Release manifest 要求 Updater 至少为 1.0.8：v1.0.6/v1.0.7 的 Updater 含上述缺陷，页面在线向导会提示先在主机执行 updater-only 升级（见部署文档），Updater 升级后 App 保持旧版本即可继续页面在线升级；未受影响的 systemd 部署继续使用 server bootstrap。
## [1.0.7] - 2026-08-31

### Changed

- 报告导出由 UTF-8 BOM CSV 改为 ClosedXML 生成的多工作表 XLSX 工作簿：包含“报告概览”、“Key 明细”、“用户汇总”、“数据说明”工作表，任一区间采集失败时额外输出“采集异常”工作表；每个工作表使用正式 Excel Table 与筛选、冻结表头（明细表同时冻结关键标识列）、固定列宽、日期/数值格式、打印设置和高对比配色，不含合并单元格、图片或宏。
- 邮件投递改为附日期命名的 XLSX 工作簿；报告下载接口和钉钉/飞书限时下载链接统一下发 XLSX（`/api/v1/reports/{id}/xlsx`、`/api/v1/report-downloads/xlsx?token=...`）。

### Security

- XLSX 中所有不受信文本（包括以 `=`、`+`、`-`、`@` 开头的字符串）均显式保存为文本类型，防止公式注入；超过 15 位的整数与 ID 按文本保存以避免精度丢失。

## [1.0.6] - 2026-08-29

### Changed

- Release manifest schema v2 分别签名 OCI image config digest 和 manifest/index target digest。

### Fixed

- 修复 Docker 29 containerd image store 导入离线镜像后本地 `.Id` 变化导致安装和在线更新误拒绝。

### Security

- `docker load` 前校验签名归档的唯一 tag、config blob 和 target descriptor，加载后仅接受两种已签名 digest，并继续校验平台、版本和角色。

## [1.0.5] - 2026-08-29

### Changed

- Docker Compose bootstrap 以普通用户下载并校验 bundle，提供进度、网络重试和断点续传，仅在 Docker 与系统安装阶段提权。
- Docker 镜像加载阶段分别显示 App 和 Updater 状态。

## [1.0.4] - 2026-08-29

### Changed

- 新安装的 systemd 和 Docker Compose 部署默认使用主机端口 `8081`，systemd bootstrap 支持 `SUB2API_REPORT_PORT` 显式覆盖并在更新时保留现有端口。

### Fixed

- systemd 安装健康检查使用实际配置端口并验证 unit 处于 active，防止端口被其他服务占用时误判成功。
- 调整 systemd unit reload 顺序，避免更新时出现配置已变化的无效警告。

## [1.0.3] - 2026-08-29

### Fixed

- 服务器 bootstrap 以普通用户完成 Release 下载与 checksum 校验，仅在系统安装阶段内部调用 `sudo`，避免提权后丢失用户网络环境。
- 初始化教程分别提供 systemd 与 Docker Compose 的初始化码筛选和重新生成命令。
- 修复官方 HTTP 部署在 Production 环境下因 Secure-only antiforgery Cookie 导致管理员初始化返回 `500`。

## [1.0.2] - 2026-08-28

### Changed

- 服务器包改为 App、Migrator、CLI 共享一套 self-contained .NET Runtime，压缩体积由约 147 MiB 降至约 54 MiB。
- 服务器 bootstrap 显示下载进度、已下载字节、重试次数及安装阶段。

### Fixed

- 服务器 bootstrap 仅在缺少 native runtime 时安装运行库，不再安装 ICU/OpenSSL 开发包。
- GitHub Release、server package 和 checksums 下载支持 TLS/网络错误重试及断点续传。

## [1.0.1] - 2026-08-28

### Added

- 增加无需 Docker 和 .NET Runtime 的 self-contained systemd 服务器发行包及一键安装、更新和回滚。
- 增加 Docker Compose 手动准备模式，允许显式管理容器启动和停止。

## [1.0.0] - 2026-08-28

### Added

- 增加持久化报告计划、Quartz 月报调度、窗口冻结、规范化执行记录和任务级重试。
- 增加滚动 7/30 日、上一自然周、上一自然月和手工自定义完整自然日窗口。
- 增加 schema v4 不可变报告快照、UTF-8 BOM 动态 CSV 和限时下载授权。
- 增加邮件、钉钉和飞书组合投递、逐渠道状态、失败补发与结果审计。
- 增加 GitHub Actions 质量门和签名 Release workflow。
- 增加不依赖公共容器 Registry 的 linux/amd64 离线镜像归档、完整安装 bundle、SBOM 和 artifact attestation。
- 增加生产 bundle 的签名校验、一键安装和手工部署契约更新脚本。
- 项目采用 Apache License 2.0，并在源码、容器镜像和 Release bundle 中携带许可证。
- 增加管理页面 App-only 在线升级、维护模式、SQLite 一致性备份、连续健康验证和自动回滚。

### Changed

- 报告统计主体统一为 Sub2API 用户到 API Key，稳定标识使用 `user_id + api_key_id`。
- 生产 Docker Compose 只加载经过校验的本地镜像，源码开发使用独立 Compose override 构建。

### Removed

- 移除人员档案和人员到 API Key 归属功能，历史报告快照保持不可变。

### Security

- Release manifest 使用独立 RSA 发布密钥签名，并校验归档 SHA-256、大小、架构、版本和镜像 ID。
- 手工部署契约更新在迁移前创建独立数据库备份，并在失败回滚前验证备份哈希。
- App 容器不挂载 Docker Socket；Updater 通过 instance/container allowlist、固定 API 和 non-root Socket group 隔离高权限操作。
- 增加公开安全政策，并将未修复漏洞引导至 GitHub Private Vulnerability Reporting。

[Unreleased]: https://github.com/lohaaa/sub2api-report/compare/v1.1.3...HEAD
[1.1.3]: https://github.com/lohaaa/sub2api-report/compare/v1.1.2...v1.1.3
[1.1.2]: https://github.com/lohaaa/sub2api-report/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/lohaaa/sub2api-report/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/lohaaa/sub2api-report/compare/v1.0.8...v1.1.0
[1.0.8]: https://github.com/lohaaa/sub2api-report/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/lohaaa/sub2api-report/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/lohaaa/sub2api-report/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/lohaaa/sub2api-report/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/lohaaa/sub2api-report/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/lohaaa/sub2api-report/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/lohaaa/sub2api-report/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/lohaaa/sub2api-report/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/lohaaa/sub2api-report/releases/tag/v1.0.0
