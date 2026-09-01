# 实施计划

## 1. 当前基线

- 当前版本：`v1.2.0`。
- 目标：交付公开、单管理员、单实例的 Sub2API Codex 用量报告系统。
- 后端：.NET 10、ASP.NET Core、EF Core、SQLite、Quartz.NET。
- 前端：React、TypeScript、Vite、shadcn/ui。
- 部署：self-contained systemd 或 App-only Docker Compose。
- 更新：仅由宿主机 bootstrap 命令执行；应用和管理页面不提供更新入口。

核心约束：

- 运行期业务配置写入 SQLite 并动态生效；
- 报告生成前自动同步 Sub2API 用户和 API Key；
- 报告快照不可变，投递和重试保留规范化执行记录；
- Release 使用签名 manifest、checksum、SBOM 和 provenance；
- Docker 生产容器不挂载 Docker Socket；
- 更新前必须备份数据库，健康验证失败必须自动回滚。

## 2. 已完成里程碑

| 里程碑 | 结果 |
| --- | --- |
| 基础工程 | 模块化单体、React SPA、SQLite、Identity、初始化码、审计和统一质量门 |
| Sub2API 接入 | 连接测试、用户/Key 同步、选择范围、稳定 `user_id + api_key_id` 标识 |
| 报告引擎 | 动态自然日窗口、采集重试、用户→Key 聚合、schema v4 不可变快照 |
| 导出与投递 | 多工作表 XLSX、邮件附件、钉钉/飞书限时下载、失败分片补发 |
| 调度 | Quartz 持久化、每月 1–31 日、短月取月末/跳过、幂等运行和重启恢复 |
| 直接部署 | 无 Docker/.NET Runtime 的 self-contained server 包、systemd bootstrap 与回滚 |
| Docker 部署 | 不依赖 Registry 的 linux/amd64 单 App 离线 bundle、bootstrap 与回滚 |
| 发布 | Changelog 驱动 Release notes、RSA 签名、checksum、App SBOM 和 artifact attestation |

## 3. v1.2.0 部署收敛

v1.2.0 将 Docker deployment contract 升级为 v2：

- Compose 只包含 App 和 `app-data` 卷；
- 移除常驻 Updater、Docker Socket、control network 和共享 token；
- 移除更新 API、维护模式、前端“系统更新”页面和 UpdateContracts；
- manifest schema v4 只描述 App、数据库迁移目标和 Release notes；
- bundle 只携带一个 App 镜像归档；
- 删除 Candidate workflow，正式 Release 只构建和验证一次目标制品。

### v1 到 v2 迁移门

真实 v1.1.3 Release bundle 必须覆盖以下事务：

1. 安装并启动 v1 App+Updater；
2. 保存旧 Compose、`.env`、发布控制文件、App/Updater image ID；
3. 停止 App 并创建带 checksum 的 SQLite 备份；
4. 注入 v2 App 健康失败，验证旧 Compose、双镜像标签和数据库恢复；
5. 再执行成功迁移，验证新 App 健康后才移除旧 Updater 容器；
6. 验证旧 `updater-state` 卷、token 文件和备份仍存在；
7. 验证新 Compose 只有 App，且没有 Docker Socket 挂载。

迁移失败不得留下半更新控制文件或损坏数据库。成功迁移不得自动执行不可逆数据清理。

## 4. 质量门

### 每次变更

- C#：format、Release build、全部 .NET tests；
- 前端：TypeScript typecheck、oxlint、Vitest、Vite production build；
- 脚本：`bash -n`、shellcheck、release-lib/bootstrap/server 脚本测试；
- Compose：配置解析和 linux/amd64 App image build；
- 文档：版本、Changelog、命令、Release asset 名称和部署契约一致性。

仓库标准命令：

```bash
pnpm quality:dotnet
pnpm quality:web
pnpm quality
```

### 正式 Release

1. 校验 Tag、项目版本和 Changelog 章节一致；
2. 运行完整 `pnpm quality` 和部署脚本测试；
3. 构建一次目标 App、server package 和签名离线 bundle；
4. 独立验证 bundle 签名、镜像 archive metadata 和加载后标签；
5. smoke 测试 self-contained server 和全新 Docker 安装；
6. 对 contract 变化执行真实 N-1 迁移与失败回滚；
7. 对 App 镜像执行 Critical 漏洞扫描并生成 SBOM；
8. 对发布资产生成 provenance；
9. 所有门通过后创建 GitHub Release。

## 5. 测试矩阵

| 类型 | 必须覆盖 |
| --- | --- |
| 单元 | 日期窗口、短月策略、聚合、状态机、XLSX 安全、签名/归档校验 |
| 集成 | SQLite migration、Identity、Sub2API stub、报告全流程、Quartz 配置投影 |
| 前端 | 初始化、登录、设置、用户/Key、报告、渠道、计划和错误状态 |
| 部署脚本 | 参数校验、签名 bundle、镜像 ID 解析、`.env` 保留、全新安装 |
| 更新事务 | 数据库备份、健康失败回滚、标签恢复、v1→v2 成功迁移 |
| 安全 | Secret 扫描、App Critical CVE、无 Docker Socket、公开仓库数据政策 |

不使用真实生产凭证、数据库、报告或日志做测试。所有 SQLite、Data Protection、日志、截图和跟踪文件必须放在系统临时目录并在验证后清理。

## 6. Definition of Done

一个版本只有同时满足以下条件才算完成：

1. 产品行为、API、前端和文档一致；
2. migration 可在一次性 SQLite 上前向执行，回滚路径有数据库备份；
3. `pnpm quality` 通过；
4. 部署脚本测试和 Compose 校验通过；
5. Release assets 包含 Apache-2.0 许可证、签名、checksum、SBOM 和 provenance；
6. N-1 兼容或明确的 contract 迁移已用真实公开 bundle 验证；
7. Changelog 包含面向用户的 Changed/Removed/Security/Upgrade 说明；
8. 未向仓库写入 Secret、SQLite、备份、日志或其他运行产物。
