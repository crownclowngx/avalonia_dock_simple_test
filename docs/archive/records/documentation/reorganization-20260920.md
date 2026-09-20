# 2026-09-20 Host 文档整理与漂移修正

> 用途：记录本次文档批量整理、人工验收收口和验证范围。日期：2026-09-20。遵循[文档维护规则](../../../maintenance/documentation.md)；自动化最终结果单独写入[非嵌入证据](reorganization-20260920-evidence.json)。

## 基线与人工结论

- 整理前 HEAD：`94b98c698f0f9156ca881fa0ea996a0186123e65`（master），主仓工作树干净。
- 项目所有者明确确认已经实际手工使用、允许标记人工验收通过，统一记录在 [Host 人工验收确认](../host/manual-acceptance-20260920.md)。
- 收口 Host 通用桌面体验的重复待办；外部插件专有业务、macOS 实验、正式发布及未来候选仍由[现行待办](../../../roadmap/README.md)跟踪。
- 本次修改仅涉及主仓 Markdown 及方案 SVG 原样搬迁，不修改产品代码、测试、Gate、SDK/API、版本配置或外部插件仓库。

## 整理与漂移修正

1. 按使用、插件开发、Host 维护、证据与待办重写总导航和 Host 入口，移除逐版本叠加的重复说明。
2. 11 份已完成方案移入 `archive/plans`，连同一份 SVG 原样迁移；5 份单次部署说明移入对应 `archive/records`。可复用的专项测试矩阵保留在 maintenance。
3. 增加当前[本机部署入口](../../../maintenance/local-deployment.md)，收录遗漏的 V16/V17 交付证据；旧部署目录及备份政策保持原阶段语义。
4. 集中基线补齐 V10–V17 实现状态与 Avalonia.Base 布局运行时补丁身份；官方 SDK 编译依赖、产品/包版本和最低兼容版本保持原有事实。
5. 修正应用内工作台帮助中“禁止浮动”、Layout V2、不可用插件整体隔离和旧“插件状态”菜单名称，补入当前浮窗、看板开关、创建目标与重启行为；架构帮助改为 manifest/Document schema 2、Layout schema 3。
6. 内部架构的插件发现顺序补齐身份预检后的启用过滤；测试映射中的不存在类型 `PluginProviderOwnerTests` 改为实际的 `HostLifecycleOwnershipTests`。
7. 专项入口同步所有者人工验收结论；15 份原开发记录仅追加带日期的后续说明，原始测试结果和 JSON 字节不变。原计划正文中的未执行项仍是历史快照，不虚构逐项新执行记录。

## 验证与证据

批量检查涵盖全部现存受跟踪及新增 Markdown 的本仓文件链接、标题锚点、帮助链接、可选邻仓引用和模板边界；核对当前文档测试类、历史非 Markdown 资产摘要及搬迁正文。搬迁正文比较只忽略新增归档说明和重算后的链接地址，历史结果必须保留。

应用内八个帮助章节、四篇理论原文和 Host 架构原文的固定入口保留。文档嵌入 Host，先完成本文与所有 Markdown，再运行完整本地开发验证；最终只更新非嵌入证据 JSON，记录输入摘要、实际运行、通过/失败/跳过及结果范围。

```powershell
python artifacts/documentation-audit/20260920/check-docs.py
git diff --check
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

扫描脚本与完整日志属于本次本地 `artifacts/documentation-audit/20260920` 产物，不成为产品或 Gate 新规则；可持久查阅的结果保存在本记录相邻 JSON。完整 verify 包含现有帮助读取与渲染测试，不把自动化结果称为本次新增人工操作。

没有执行 seal、发布覆盖率、Windows 发布 Smoke、部署、上传或 Git 提交/推送。现有 Gate 的 V2 Smoke 断言和 API 发布政策列为真实发布待办，本次不为文档整理改变代码或放宽阈值。

## 搬迁清单

| 原位置 | 归档位置 |
| --- | --- |
| `docs/roadmap/host-dock-area-fill-implementation-plan.md` | `docs/archive/plans/host-dock-area-fill-implementation-plan.md` |
| `docs/roadmap/host-document-cross-window-layout-crash-fix-plan.md` | `docs/archive/plans/host-document-cross-window-layout-crash-fix-plan.md` |
| `docs/roadmap/host-v11-floating-windows-and-layout-v3-plan.md` | `docs/archive/plans/host-v11-floating-windows-and-layout-v3-plan.md` |
| `docs/roadmap/host-v11-p1-tool-split-fix-plan.md` | `docs/archive/plans/host-v11-p1-tool-split-fix-plan.md` |
| `docs/roadmap/host-v11-p2-tool-window-close-fix-plan.md` | `docs/archive/plans/host-v11-p2-tool-window-close-fix-plan.md` |
| `docs/roadmap/host-v12-internal-refactor-plan.md` | `docs/archive/plans/host-v12-internal-refactor-plan.md` |
| `docs/roadmap/host-v13-plugin-enablement-plan.md` | `docs/archive/plans/host-v13-plugin-enablement-plan.md` |
| `docs/roadmap/host-v14-automatic-restart-plan.md` | `docs/archive/plans/host-v14-automatic-restart-plan.md` |
| `docs/roadmap/host-v15-startup-splash-plan.md` | `docs/archive/plans/host-v15-startup-splash-plan.md` |
| `docs/roadmap/host-v16-document-floating-close-and-creation-target-plan.md` | `docs/archive/plans/host-v16-document-floating-close-and-creation-target-plan.md` |
| `docs/roadmap/host-v17-readability-refactor-plan.md` | `docs/archive/plans/host-v17-readability-refactor-plan.md` |
| `docs/maintenance/host-v12-local-deployment.md` | `docs/archive/records/host-v12/local-deployment-guide.md` |
| `docs/maintenance/host-v13-local-deployment.md` | `docs/archive/records/host-v13/local-deployment-guide.md` |
| `docs/maintenance/host-v14-local-deployment.md` | `docs/archive/records/host-v14/local-deployment-guide.md` |
| `docs/maintenance/host-v15-local-deployment.md` | `docs/archive/records/host-v15/local-deployment-guide.md` |
| `docs/maintenance/dock-cross-window-layout-deployment.md` | `docs/archive/records/dock-area-fill/cross-window-layout-deployment-guide.md` |
| `docs/roadmap/assets/host-v15-startup-feather.svg` | `docs/archive/plans/assets/host-v15-startup-feather.svg` |
