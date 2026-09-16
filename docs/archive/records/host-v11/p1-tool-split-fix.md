# V11-P1 Tool 分割故障与修复记录

> 起始日期：2026-09-16。基线：`0525321`。本记录追加 V11 已部署版本之后的源码补丁事实；本补丁不覆盖安装目录。范围见 [P1 计划](../../../roadmap/host-v11-p1-tool-split-fix-plan.md)。

## 已确认故障

用户在已部署单 EXE 中将 Tool 放到另一个 Tool 下方，Windows Application 事件 1026 记录未处理 `ArgumentOutOfRangeException`。调用链为 DockService.SplitToolDockable → HostDockFactory.SplitToDock → ToolDockCoordinator.FlattenTemporarySplit → InsertDockable。

旧逻辑把 Tool 局部上下分割归一化到全宽区域；移除临时容器时 Dock 同时清理孤立分隔条，即使 collapse=false 也会缩短父列表。再用移除前的索引插入会越界；未越界时仍可能丢失分隔条或移动位置。纯模型复现与实际崩溃堆栈一致，Document 分割不会进入该分支。

## 开发验证约定

先提交能失败的正式回归测试，再修改生产逻辑。专项与最终完整 verify 的实际结果在完成后登记。最终文档需要嵌入 Host，最终验证后只回填非嵌入 JSON，不沿用修改文档之前的产物身份。

真实桌面鼠标、多屏及外部插件业务需独立观察；本记录不将纯模型或 Headless 当作真实桌面通过。不运行 AIFLOW、Windows CI、seal 或发布门禁。

## P1-1 正式失败基线

执行 `dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~P1 --logger "trx;LogFileName=p1-red.trx" --results-directory artifacts/host-v11-p1/red`：编译无警告，30 项均失败、无跳过。24 项覆盖 Tool 局部上下分割，6 项覆盖保留全宽停靠的容器替换；失败包括原索引越界、错误全宽迁移、分隔条丢失和引用不一致。此阶段故意保留生产缺陷，不将红灯记录为通过。
