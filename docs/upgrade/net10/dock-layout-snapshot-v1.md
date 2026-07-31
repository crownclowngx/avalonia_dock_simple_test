# Dock 结构布局快照 V1

## 设计边界

布局文件固定为：

`%LOCALAPPDATA%\MyAvaloniaManagement\layout-v1.json`

它只保存宿主可重建的 Dock 结构：

- 稳定布局节点 ID 及左右面板比例；
- 稳定 Tool ID、所属 ToolDock、顺序和显隐；
- 旧版本工具浮动状态及窗口边界（仅用于兼容读取，当前版本保存时统一归一化为非浮动）；
- 活动工具 ID。

它明确不保存 Document、密码、媒体路径、标题、播放状态、插件表单值或其他业务数据。重启后只创建默认欢迎 Document，不会自动重开历史 Document。

## 稳定 ID

- `Root`
- `Workspace`
- `WorkspaceColumns`
- `WorkspaceCenterRows`（兼容 ID；当前语义为全宽 `WorkspaceRows`）
- `LeftPane`
- `LeftTools`
- `TopPane`
- `TopTools`
- `Documents`
- `BottomPane`
- `BottomTools`
- `RightPane`
- `RightTools`

旧代码仍可用 `Files` Locator 查找 DocumentDock，但持久化 ID 固定为 `Documents`。

## 校验与回退

读取后、修改 Dock 树前必须完整校验：

- `schemaVersion` 必须为 `1`；
- ID 只能包含 ASCII 字母、数字、点、短横线和下划线，且不得重复；
- 同一 ToolDock 内顺序不得重复或为负数；
- 面板比例必须为有限值且位于 `0.05–0.95`；
- 旧快照中的浮动工具必须可见并具有有限、合理的窗口边界；
- 快照引用的插件工具和稳定 Dock 节点必须存在。

JSON 损坏、未知版本、重复 ID、无效比例或插件缺失时，原文件改名为带 UTC 时间戳的 `.invalid.bak`。日志只写固定错误码和通过校验的稳定 ID，不记录 JSON 内容。宿主随后使用完整默认布局启动，不应用部分状态。

当前主工作区禁止 Document 和 Tool 创建独立浮动窗口。合法的 V1 旧快照如果包含
`isFloating: true`，启动时会按照对应的 `dockId` 和 `order` 自动放回主窗体内的
ToolDock；显隐与活动项继续恢复。下次保存时写为 `isFloating: false` 且不再写入
`floatingBounds`，无需提升 schema 版本。

当前布局支持 Left、Right、Top、Bottom 四个稳定 ToolDock。Top/Bottom 位于完整
Dock 工作区的上方和下方，横跨 Left、Document、Right，显示时默认各占工作区高度的
20%；没有可见 Tool 时通过
Dock 的空布局折叠机制收缩为零高度。旧版只包含左右节点的 V1 快照会在内存中迁移：
元数据声明为 Top/Bottom、但因旧实现被记录到 LeftTools 的工具将回到正确区域并显示，
原有左右面板比例和其他工具状态保持不变；迁移结果在下次正常保存时写回。
用户通过拖拽拆分产生的无稳定 ID Top/Bottom ToolDock 会在拖拽完成时立即迁移到
全宽稳定区域，保存时继续按实际 Alignment 归一化；重启恢复前若目标节点尚不存在，
宿主会先重建对应节点。最后一个 Tool 隐藏导致空 ToolDock 被 Dock 移除时，再次显示
也使用相同机制恢复。

## 写入与生命周期

写入先在同目录创建唯一临时文件并强制刷新，再用原子替换更新正式文件；无论成功失败都清理临时文件。

启动顺序：

1. 读取并校验快照；
2. 创建默认 Dock 树；
3. 执行 `InitLayout`；
4. 主窗口 `Opened` 后应用工具结构。

退出时从当前 Dock 树捕获结构、再次校验并原子保存。保存失败只记录固定错误码，不能阻止 Document Scope、插件或进程退出。
