# Dock 布局快照 V3

> 用途：V11 工具布局的唯一线格式与恢复契约。状态：实施中，编解码、V2 纯转换与文件 Store 已实现，生产切换和恢复集成仍在进行。核对日期：2026-09-16。实施结果见 [V11 记录](../archive/records/host-v11/development-acceptance.md)。

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

保存经 UI 捕获、后台串行提交；恢复/拆窗期间禁止捕获半成品。数据根只有一个布局写入者，其余实例只读；文件事务、窗口关闭及实际验证结果随实施更新到专用维护指南和 V11 记录。
