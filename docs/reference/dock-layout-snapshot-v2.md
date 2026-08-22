# Dock 布局快照 V2

> 当前正式生产契约，建立于 Managed Plugin V2 G8，并由 G14 Windows 真实窗口门禁签署。历史
> `layout-v1.json` 只保留为文件事实，Host 不读取、迁移、覆盖或隔离它。

## 文件与所有权

- 文件名固定为 `layout-v2.json`，schema 固定为 `2`。
- 默认数据根是 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`。
- `MYAVALONIA_DATA_DIRECTORY` 表示完整数据根，不追加产品名或 `v2`。
- 文件只保存 Host 能从 Registry 和 Dock 树重建的布局状态；Document、标题、路径、payload、播放/下载状态和插件表单值禁止进入布局。
- `DockLayoutStore` 只负责路径、原子事务、读写失败和 `.invalid.bak` 隔离；`DockLayoutSnapshotV2Json` 只负责严格线格式；`DockLayoutSnapshotValidator` 与运行时 Validator 分别负责结构值和当前 Registry/Dock 事实。

## 唯一线格式

```json
{
  "schemaVersion": 2,
  "panes": [
    {
      "id": "LeftPane",
      "proportion": 0.2
    }
  ],
  "tools": [
    {
      "id": "myavalonia.host.tool.file-system-tree",
      "dockId": "LeftTools",
      "order": 0,
      "isVisible": true,
      "isPinned": false
    }
  ],
  "activeToolId": "myavalonia.host.tool.file-system-tree"
}
```

字段集合必须精确匹配：

- 根：`schemaVersion`、`panes`、`tools`、`activeToolId`；
- Pane：`id`、`proportion`；
- Tool：`id`、`dockId`、`order`、`isVisible`、`isPinned`。

所有字段必需，只有 `activeToolId` 可以为 `null`。未知、重复、缺失、大小写错误、错误类型、注释和尾随逗号全部拒绝。读取端不依赖字段顺序，写入端按上述固定顺序输出。

V2 没有 `isFloating`、`floatingBounds` 或任何其他浮动字段，也没有 V1 Migrator、历史短 ID、`Files`、GUID 或别名归一化。Dock 运行时仍禁止创建浮动窗口，但“禁止浮动”是工作区行为，不是持久化字段。

## 稳定结构与状态

可持久化 Pane 为 `LeftPane`、`TopPane`、`BottomPane`、`RightPane`；Tool Dock 为 `LeftTools`、`TopTools`、`BottomTools`、`RightTools`。

- `proportion` 必须是有限数并位于 `0.05–0.95`；
- Pane ID、Tool ID 不得重复；
- 同一 `dockId` 内 `order` 必须非负且唯一；
- `isPinned: true` 必须同时 `isVisible: true`；
- `activeToolId` 非空时必须引用快照中的 Tool；
- 展开是 `isVisible=true,isPinned=false`；隐藏是 `false,false`；固定收起是 `true,true`。

## 恢复事务与失败语义

恢复顺序固定为：

1. 从 `layout-v2.json` 严格读取并完成结构值校验；
2. 创建完整默认 Dock 树；
3. 在调整任何 Pane 前检查每个 Tool 已注册、生命周期可用且实例已经完整创建；
4. 补建快照合法需要的稳定四向 Dock；
5. 验证 Pane/Dock 运行时结构；
6. 一次应用比例、位置、顺序、隐藏、Pinned 和活动项。

任一插件未安装、生命周期初始化失败/超时、Tool 激活缺失、Pane/Dock 不存在、比例或顺序非法，都会隔离整份 V2 文件并使用默认布局。贡献可用性检查发生在 Dock 结构调整前；应用阶段异常则丢弃已经修改的临时默认树并重建新默认树，因此不会把部分状态发布给窗口。

损坏文件改名为 `layout-v2.<UTC>.invalid.bak`。诊断只记录固定错误码、通过格式检查的稳定 ID、阶段和异常类型，不记录 JSON 正文。保存继续使用同目录临时文件、强制刷新和原子替换。

## V1 保留边界

同一数据根中的 `layout-v1.json` 可以继续存在。V2 只查找 `layout-v2.json`；不会把 V1 改名为坏文件，也不会读取后写成 V2。回滚 G8 代码同样不得恢复 V1 reader，避免回滚操作改变用户历史文件。
