# G4：任务中心产品化

> 2026-08-02 复核更新：基线 `eb5c20a` 的持久化模型/UI 状态混用、默认跳过删除确认、GUID 筛选文案和常驻批量按钮已重构。任务卡使用独立 `DownloadTaskItemViewModel`，选择按 TaskId 跨筛选保持；删除默认仅移除记录，临时文件/成品均需显式勾选；增加今天/7天/30天筛选、180ms 搜索防抖、中文选项、加载/空/错误状态、数值速度/ETA、480px 响应式布局和虚拟化列表。

> 实施日期：2026-08-02
>
> 状态：已完成
>
> 平台范围：Windows、Linux；与 G3 相同

## 1. 完成目标

G4 解决的是任务中心从"能用"到"好用"的产品化问题：

- 将非虚拟化 `ItemsControl` 替换为 Avalonia 原生虚拟化 `ListBox`，100 个任务时仅实例化可视区域容器。
- 支持按标题模糊搜索、状态分组、Document 和创建时间筛选。
- 支持按创建时间、状态和标题排序（升序/降序）。
- 支持多选（CheckBox）、全选当前筛选结果和批量命令。
- 展示预计大小、已下载大小、质量、输出路径和错误摘要。
- 批量删除、重新开始等破坏性操作展示任务数量并要求用户确认。
- "全选筛选结果"只作用于命令开始时的结果快照。

## 2. 关键决策

### 2.1 纯函数筛选排序引擎（SRP）

将筛选排序逻辑提取为独立的 `TaskFilterSortEngine` 静态类，与 VM 状态管理完全解耦。

**为什么不用 CollectionViewSource**：Avalonia 11 的 `DataGridCollectionView` 耦合 DataGrid 控件，且引入复杂的增量更新语义。100 条任务规模下 LINQ 重填 < 1ms，纯函数 + ObservableCollection 重填策略更简洁、更可测试。

### 2.2 IsSelected 放在模型上而非独立 SelectionManager

`DownloadTaskRecord` 新增 `[ObservableProperty] bool _isSelected`，CheckBox 直接绑定。

**为什么不用独立 SelectionManager**：100 条规模下多选逻辑简单（遍历设标志），单独类增加间接层但收益不足。模型已继承 `ObservableObject`，CheckBox 绑定需要 INPC 通知，直接放在模型上最简洁。

### 2.3 确认服务通过 DIP 注入（IConfirmationService）

破坏性批量操作（删除/重新开始）执行前调用 `IConfirmationService.ConfirmAsync`。

**为什么不用内联确认条**：接口注入使测试可以验证"确认通过"和"确认拒绝"两条路径，且未来可替换为不同 UX 形态（对话框/Toast/内联条）而不改动 VM 代码。构造函数参数可选（= null），未注入时 fallback 到 `NullConfirmationService`（始终确认），保持向后兼容。

### 2.4 进度更新不触发集合重建

`TaskProgressChanged` 事件处理只修改对象属性（INPC 自动通知 UI），不调用 `ApplyFilterAndSort()`。仅在状态变更、任务增删、筛选条件变化时重建 `FilteredTasks`。

**为什么不用 DispatcherTimer 节流**：Coordinator 已有 500ms 进度节流，UI 侧再加节流增加复杂度且引入感知延迟。进度更新只修改属性不触发集合重建即可满足性能要求。

### 2.5 O(1) 任务索引

使用 `Dictionary<string, DownloadTaskRecord> _taskIndex` 替代原有的 `FirstOrDefault` 线性查找。

**为什么**：5 并发下载时进度回调频率高（10次/秒），Dictionary 查找语义更清晰且性能确定。

### 2.6 批量操作期间暂停事件驱动刷新

`_isBatchOperating` 标志在批量命令执行期间暂停 `TaskStatusChanged` 事件触发的 `ApplyFilterAndSort()`，完成后一次性刷新。

**为什么**：批量删除 100 个任务时，每次删除都触发状态变更事件，如果每次都重建集合会导致 100 次无意义的 UI 刷新和闪烁。

## 3. 代码边界

| 文件 | 职责 |
|------|------|
| `ViewModels/BiliScheduler/TaskFilterSortEngine.cs` | 纯函数筛选排序引擎 + TaskFilterCriteria + TaskSortField |
| `ViewModels/BiliScheduler/SchedulerTaskListViewModel.cs` | 核心重构：筛选/排序/多选/批量命令/确认机制 |
| `Models/DownloadTaskRecord.cs` | +IsSelected +计算属性（TotalExpectedBytes/QualityDisplayText/FullOutputPath） |
| `Services/Infrastructure/IConfirmationService.cs` | 确认服务接口 + NullConfirmationService |
| `Converters/ByteSizeConverter.cs` | 字节数 → 人类可读格式转换器 |
| `Views/BiliScheduler/SchedulerTaskListView.axaml` | ListBox 虚拟化 + 筛选栏 + 批量操作栏 + 扩展字段 |
| `Views/BiliSchedulerToolView.axaml` | 状态摘要增加筛选计数 |
| `ViewModels/BiliSchedulerToolViewModel.cs` | 传递 IConfirmationService |

## 4. 筛选排序流程

```
用户操作（搜索/筛选/排序）
    │
    ├─ [ObservableProperty] setter 触发 OnXxxChanged
    ├─ 调用 ApplyFilterAndSort()
    │     │
    │     ├─ 构造 TaskFilterCriteria（TitleContains, StatusGroup, DocumentId）
    │     ├─ 解析 SortBy → (TaskSortField, bool descending)
    │     ├─ 调用 TaskFilterSortEngine.Apply(Tasks, criteria, sortField, descending)
    │     │     │
    │     │     ├─ WHERE: 标题包含 AND 状态分组 AND DocumentId
    │     │     └─ ORDER BY: 指定字段 + 方向
    │     │
    │     ├─ 重填 FilteredTasks（Clear + Add）
    │     └─ 更新 FilteredCount + SelectedCount
    └─ UI 自动响应 ObservableCollection 变更

Coordinator 事件
    │
    ├─ TaskProgressChanged → O(1) 索引查找 → 更新属性 → 不重建集合
    ├─ TaskStatusChanged → O(1) 索引查找 → 更新属性 → ApplyFilterAndSort()
    └─ TaskListChanged → ReloadTasksAsync()（全量重建）
```

## 5. 批量操作时序

```
用户点击"批量删除"
    │
    ├─ GetSelectedSnapshot() → 捕获当前 IsSelected=true 的任务列表
    ├─ snapshot.Count == 0 → 直接返回
    ├─ _confirmationService.ConfirmAsync(title, message)
    │     │
    │     ├─ 用户取消 → 返回，不执行
    │     └─ 用户确认 → 继续
    │
    ├─ _isBatchOperating = true（暂停事件驱动刷新）
    ├─ foreach task in snapshot:
    │     ├─ _coordinator.DeleteTaskAsync(task)
    │     ├─ Tasks.Remove(task)
    │     └─ _taskIndex.Remove(task.TaskId)
    ├─ UpdateCounts() + UpdateAvailableDocuments()
    ├─ _isBatchOperating = false
    └─ ApplyFilterAndSort()（一次性刷新）
```

## 6. 自动化验证

| 测试文件 | 数量 | 覆盖范围 |
|---------|------|---------|
| `TaskCenterG4Tests.cs`（筛选引擎） | 8 | 标题模糊/状态分组/Document/组合筛选/排序/边界 |
| `TaskCenterG4Tests.cs`（VM 集成） | 8 | 100任务筛选/快照语义/批量删除确认/批量重试/进度不重建/状态重建/Document提取 |
| `TaskCenterG4Tests.cs`（转换器） | 9 | ByteSizeConverter 各量级 |
| `TaskCenterG4Tests.cs`（模型） | 3 | 计算属性/质量映射/路径拼接 |
| `TaskCenterG4Tests.cs`（性能） | 2 | 100任务筛选<5ms/100任务批量操作 |
| `TaskCenterG4Tests.cs`（排序解析） | 1 | ParseSortBy 各键值 |
| 现有测试回归 | 225 | 全部通过，无破坏性变更 |
| **总计** | **256** | |

## 7. 退出条件验证

| 退出条件 | 验证方式 |
|----------|----------|
| 100 个任务时仍可流畅滚动、筛选和批量操作 | ListBox 虚拟化 + 性能测试（筛选 < 5ms，批量删除 < 10s） |
| 可以快速定位失败、已中断和等待登录任务 | 状态分组筛选（"failed"/"interrupted"/"waiting_login"） |
| "全选筛选结果"只作用于命令开始时的结果快照 | 快照语义测试：全选后修改筛选，SelectedCount 不变 |
| 批量删除、重来等破坏性操作会展示任务数量并要求确认 | FakeConfirmationService 验证：消息包含"N 个任务"，拒绝时不执行 |

## 8. 明确限制与后续工作

- 筛选为内存 LINQ 实现，100 条级别 < 1ms。若未来任务量增长到 1000+，可迁移到 SQLite 分页查询，`TaskFilterSortEngine` 纯函数接口不变。
- 生产环境已注册 `AvaloniaUserPromptService`；无 UI owner 时采用安全取消。
- 进度节流间隔硬编码 500ms（G3 决策）。G4 未修改此值，后续可根据并发数动态调整。
- 任务删除会清理 Tracker 与 UI 索引；ETA 使用数值 `BytesPerSecond` 计算。
- Document 筛选优先显示工作台标题，旧记录回退为截断后的 Document ID。
