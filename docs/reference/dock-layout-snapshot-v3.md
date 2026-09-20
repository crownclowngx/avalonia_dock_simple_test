# Dock 布局快照 V3

> 用途：V11 工具布局的唯一线格式与恢复契约。状态：当前，已接入生产恢复、自动保存和退出流程；Host 人工验收已[确认通过](../archive/records/host/manual-acceptance-20260920.md)。核对日期：2026-09-20。实施结果见 [V11 记录](../archive/records/host-v11/development-acceptance.md)。

## 文件与边界

主文件为 `layout-v3.json`，上一有效文件为 `layout-v3.json.bak`。默认数据根继续为 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`；`MYAVALONIA_DATA_DIRECTORY` 仍表示完整数据根。

schema 固定为 3，与产品、SDK、manifest 和 Document envelope 独立。Document 文件、内容、标题、PageId 和重开清单不进入布局。主窗口的 documents 节点只是一个中心停靠占位；纯文档浮窗不持久化。

## 严格字段集合

以下全部字段必需；注明可空的值也必须显式写 `null`。拒绝未知、缺失、重复和大小写错误的字段、错误类型、注释及尾随逗号。字段顺序不是读取要求；写出顺序确定。

| 对象 | 精确字段 |
| --- | --- |
| 根 | schemaVersion、mainWindow、floatingWindows、tools |
| 窗口 | id、bounds、root |
| bounds | x、y、width、height、maximized、screen（可空） |
| screen | x、y、width、height、scaling |
| 布局节点 | kind、id、proportion、orientation（可空）、children、toolIds、activeToolId（可空） |
| 工具 | id、state、returnDockId、returnOrder |

- `kind=split`：orientation 为 horizontal 或 vertical，children 至少两个，比例总和为 1，toolIds 为空，activeToolId 为 null。
- `kind=tools`：children 为空、orientation 为 null、toolIds 非空且有序；活动项必须是本组状态为 visible 的工具。
- `kind=documents`：只有主窗口允许且恰好一个；children/toolIds 为空，orientation/activeToolId 为 null。
- 工具 state 为 visible、hidden 或 autoHidden；autoHidden 仅允许在主窗口。
- 当前归属只由工具组的 toolIds 表达。returnDockId/returnOrder 是回到主窗口时使用的备用位置，不形成第二个当前归属。
- returnDockId 只能为 LeftTools、RightTools、TopTools、BottomTools；returnOrder 为非负整数。
- 窗口 ID 与节点 ID 各自唯一，工具不能重复占位。稳定 ID 限 128 字符，仅 ASCII 字母、数字、点、下划线和短横线。
- 节点比例为有限数，范围 `(0,1]`；分割总和误差不超过 `0.000001`。
- 文件最多 1 MiB，最多 32 个浮窗、512 个工具、1024 个节点，节点树最大深度 32；循环或多父引用被拒绝。

## 坐标

bounds 的 x/y 为屏幕像素坐标，width/height 为 Avalonia 逻辑尺寸。screen 是保存时的屏幕工作区像素矩形与缩放，允许为 null。负坐标合法；坐标绝对值不超过 10,000,000，尺寸为正且不超过 100,000，缩放范围 0.25–8。

maximized 独立记录，最大化或最小化不能覆盖正常位置和尺寸；重启不恢复最小化状态。合法但屏幕外的位置由当前屏幕策略校正，不视为坏文件。

## 最小示例

```json
{
  "schemaVersion": 3,
  "mainWindow": {
    "id": "main",
    "bounds": {
      "x": 80,
      "y": 80,
      "width": 1000,
      "height": 700,
      "maximized": false,
      "screen": null
    },
    "root": {
      "kind": "documents",
      "id": "Documents",
      "proportion": 1,
      "orientation": null,
      "children": [],
      "toolIds": [],
      "activeToolId": null
    }
  },
  "floatingWindows": [],
  "tools": []
}
```

## 迁移和恢复要求

V2 使用严格 reader 和纯转换，不调用具有隔离副作用的旧 Store.Load。原件字节保持不变，四向 Pane 比例、工具顺序、隐藏和 Pinned 转为 V3，已知退役内置工具继续精确移除。V1 不读取、修改或隔离。

V3 主文件优先，其次有效 V3 备份；只有首次无 V3 历史时导入 V2。已升级后损坏不得反复导入过期 V2。未来 schema 使用独立错误 `LAYOUT_SCHEMA_FUTURE`，宿主保留文件并采用只读默认布局。

结构损坏与插件不可用分开：前者拒绝并保留诊断副本；后者只恢复当前可用项，同时保留不可用项的布局记录供后续合并。隐藏或不可用工具的组可以保存在树中，但不因此显示空白原生浮窗。同一组保留隐藏成员的相对次序；原父分割仍可识别时保留其分支与比例。若用户已重构整段可见结构，旧父节点不再存在，则把完整保留分支邻接到当前结构，保持其窗口、组、内部次序与比例，不恢复用户已删除的可见布局。

保存经 UI 捕获、后台串行提交；恢复/拆窗期间禁止捕获半成品。数据根只有一个布局写入者，其余实例只读；文件事务、窗口关闭及实际验证见 [专项维护指南](../maintenance/floating-layout-verification.md) 与 V11 记录。


## 保存与窗口事务

V11-P2 不改变 V3 字段或迁移规则。最后一个 Tool 的按钮、关闭命令或隐藏入口，先请求窗口关闭；原生或框架拒绝时内容与活动项保持原状。获准时在窗口位置跟踪器注销前捕获分组和 bounds，框架 `CloseWindow` 的逐项隐藏纳入同一批量变更，只提交最终隐藏状态，避免 Closed 之后读到默认位置而覆盖记录。保存组件不接收按钮事件，也不负责关闭或回收窗口。

关闭外壳后仍可存在仅含隐藏工具的 floatingWindows 记录。这是恢复位置，不是要求创建空窗；重新显示目标工具时复用其原模型和 View，重启仍只恢复可见工具、不自动重开文档。专项证据见 [P2 修复记录](../archive/records/host-v11/p2-tool-window-close-fix.md)。

V11-P1 不改变 schema 或迁移规则。Tool → Tool 的上下分割及浮窗内分割保留局部结构；Tool → 主窗口 DocumentDock（含文档子组）以及明确的 WorkspaceRows/WorkspaceColumns 全局目标才使用全宽兼容策略。目标按主树对象引用判定，不能用节点同名或 Top/Bottom 操作本身推断。Document 分割保持框架语义。

布局记录的分组 ID 与固定主骨架 ID 分开：运行时全宽组恢复后可以使用 V3 分组身份，应验证上下关系、比例与工具归属，而非要求恢复为运行时 TopTools/BottomTools 别名。P1 保持原模型、View 与 Document Scope，不把拖放整理变成保存组件的职责。

日常调整在 UI 操作结束后捕获纯快照，750 毫秒防抖后后台串行提交。新修订晚于旧修订写入；失败不会把修订标为已保存，可以重试。退出前冻结捕获并排空最后提交，关闭浮窗产生的隐藏通知不会写回。

DockService 先移动内容，再调用分割，新组可能短暂未挂接。Factory 批量通知与 Dispatcher 延后捕获共同确保文件只接受最终布局，不要求一次拖放只产生一次事件。临时容器替换先插入剩余节点再移除容器，避免框架清理相邻分隔条造成过期索引；不满足整理前提时保留有效分割并记录诊断。现有恢复检查点不承诺整个拖放可回滚。

最终排空只等待不依赖 Dispatcher 的文件队列，可在无在途文档命令的原生关闭路径同步完成；有异步确认/命令时取消首次关闭并重试。保存失败保留窗口和命令入口，显示重试提示。只读实例允许正常退出并明确不保存本次布局。

恢复先验证和建立候选容器，再转移原实例。短期运行树检查点记录集合和原生窗口身份；关闭被拒绝或建立候选窗失败时恢复原容器，仍可见的窗口复用，已关闭的窗口重建外壳。检查点不会进入布局文件，也不重建 Document Scope。

`HostFloatingWindow` 沿用 Dock 原生外框和协议，增加独立快捷键/命令面板/全屏层。`HostDockFactory.InitDockWindow` 复用仍可见的同模型窗口，避免活动容器再次初始化产生重复窗口。浮窗不拥有 Runtime 或 Provider。
