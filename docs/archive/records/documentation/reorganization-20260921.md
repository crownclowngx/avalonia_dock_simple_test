# 2026-09-21 Host 文档归位与漂移修正

> 用途：记录目录调整、当前状态修正及验证范围。日期：2026-09-21。维护规则见[文档维护](../../../maintenance/documentation.md)，实际执行结果见[非嵌入证据](reorganization-20260921-evidence.json)。

## 基线与验收

整理前主仓 HEAD 为 `d9765768bc76a5c971a39447a43b20fda28ebb67`（master），工作树干净。项目所有者明确表示“我已经手工验收过主程序了”，记录为[当前 Host 人工验收通过](../host/manual-acceptance-20260921.md)，收口原 V18 桌面体验待办。未提供逐项环境日志，不补写输入法、多屏或二进制身份。

核对 Git 提交、构建属性、两次 2026-09-21 部署 JSON、V18 最终开发证据及命令面板实现：当前代码为 V18，V19 尚未实施。原开发证据中的最终 `verify` 已通过；源码身份、测试数量和原失败保持原始记录。

## 目录归位

| 原位置 | 新位置 | 原因 |
| --- | --- | --- |
| `docs/maintenance/host-v19-complexity-verification.md` | `docs/roadmap/host-v19-complexity-verification.md` | 尚未实施的验证计划与方案同层，实施后再提取可复用维护流程 |
| `docs/archive/records/host-v19/local-deployment-20260921.json` | `docs/archive/records/host-v18/local-deployment-with-v19-plan-20260921.json` | 实际交付是 V18 实现附带 V19 方案文档，按代码阶段归档，并与同日首次部署区分 |

部署 JSON 原样移动，保留当时 `recordType`、覆盖文件路径、artifact 路径、哈希及结果。帮助的八个章节、四篇理论原文、Host 架构原文保持固定入口。Layout V2 仍是现行 V3 的只读迁移输入说明，保留在 reference；已实施功能的回归矩阵继续位于 maintenance。V19 方案继续在 roadmap，不作为完成计划归档。

## 漂移修正

- 总导航、根 README、Host 入口、当前契约及专项入口统一引用新的人工验收记录；历史记录仅追加后续说明。
- 本机部署入口从 V17 更新为 V18 的两次交付，补齐原先遗漏的部署证据，并明确最新交付只附带 V19 文档。各次备份政策按原记录分别说明。
- 集中版本基线补齐 V18 实现与交付状态，V19 明确保持待实施，产品、SDK、NuGet 和数据格式版本保持原事实。
- 应用内“工作台使用”同步四类连续分组、名称左侧动作、底部效果、分组排序、中文预编辑按键及提交目标校验。
- V18 维护页改为已实施功能的回归入口，原开发阶段的原生体验未执行事实与后续所有者整体确认分开记录。
- 文档维护规则明确待实施验证计划与实际部署证据的归属，同步所有搬迁链接。

## 验证范围

全部受跟踪及本次新增 Markdown 检查本仓链接、标题锚点、帮助链接、可选邻仓引用和模板边界；旧 JSON 与非 Markdown 历史资产按 HEAD 校验字节摘要。历史 Markdown 仅新增日期说明，原正文保持。扫描排除 `.aiflow`，不读取或维护该上下文。

文档定稿后重新构建并运行 `HelpContentTests`、`HelpWindowTests`，核对嵌入 Markdown 与当前文件字节一致、新路径可读取和渲染、旧验证计划资源不再存在；运行 `git diff --check`。本次仅调整文档与证据位置，验证聚焦链接及受影响的帮助链；最终结果只写非嵌入 JSON，避免验证后再次改变已验证的 Markdown 输入。

脚本及完整日志保存在 `artifacts/documentation-audit/20260921`，持久摘要保存于本文相邻证据。未修改产品代码、测试、SDK/API、构建配置或外部插件仓库；未重新执行完整开发 `verify`、发布门禁、安装部署、上传、Git 提交或推送。
