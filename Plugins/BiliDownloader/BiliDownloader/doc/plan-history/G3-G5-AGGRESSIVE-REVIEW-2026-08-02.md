# G3–G5 激进式闭环复核记录

复核日期：2026-08-02  
Git 基线：G3 `589f5b3`、G4 `eb5c20a`、G5 `615badc`

## 1. 结论

本轮允许破坏内部 API，按“可靠性内核 → 任务中心 → 下载方案”完成重构。SQLite 旧任务原地增加 `bytes_per_second` 与 `source_document_title`，不删除任务或媒体文件；Document V1/V2 继续可读取。旧的消息构造入口保留兼容适配，但新的调度边界为不可变 `DownloadSubmission`。

## 2. 基线问题与处置

| 范围 | 基线问题 | 最终处置 |
| --- | --- | --- |
| G3 | Marker 可能被 DropOldest 丢弃，Flush 不保证最后快照 | 每任务最新完整快照缓存、单消费者、去重 Persist 信号；Flush/Stop 使用不可丢控制命令 |
| G3 | 阶段和字节分别写入，存在中间不一致 | `TaskRuntimeSnapshot` + 单次 SQL 原子更新 |
| G3 | busy/locked 后只记录日志 | 有限退避重试；Flush/Shutdown 最终失败向上抛出 |
| G3 | 仅检查 `video.tmp` / `audio.tmp` | 恢复服务汇总 `.chunkN`，并按预期长度截断 |
| G4 | UI 选择状态污染持久化模型 | 独立 `DownloadTaskItemViewModel` 保存选择与用户文案 |
| G4 | 生产环境默认确认删除 | 注册真实 Avalonia 提示服务；默认只移除记录，文件选项默认不勾选 |
| G4 | 内部筛选键和 Document GUID 直接展示 | 中文选项对象；工作台标题优先、旧记录使用截断 ID 回退 |
| G4 | 操作堆叠且窄栏不可用 | 480px 断点；窄栏保留主操作和更多菜单，宽栏展示完整阶段信息 |
| G5 | 预设仅应用部分字段、初始化存在竞态 | `DownloadProfile` 完整聚合；显式幂等 `InitializeAsync` |
| G5 | Document 实际配置会被全局默认覆盖 | 恢复优先级固定为 Document → 最后预设/全局目录 |
| G5 | 预览不随选择变化，非法/重复名称仍可提交 | 解析/勾选/标题事件驱动前三项预览；校验失败直接阻止提交 |

## 3. 结构边界

- `BiliDownloadCoordinator` 保持唯一命令入口，进度持久化、恢复、错误分类下沉为服务。
- `IUiDispatcher`、`IFileRevealService`、`IUserPromptService` 隔离 Avalonia 与平台副作用。
- `DownloadSubmission` 携带 Document ID/标题、不可变配置快照和不可变内容项。
- `DocumentSaveCodec` 统一识别版本；未知主版本只恢复安全公共字段，不伪装为 V1。
- `DownloadPresetService` 负责复制、重命名和删除规则；`PresetStore` 在同一 SQLite 事务内更新数据和索引。

## 4. 迁移与安全默认值

- SQLite 使用 `ALTER TABLE ... ADD COLUMN` 原地补列；旧行标题为空时由 UI 回退展示截断 Document ID。
- 删除任务默认 `DeleteTaskOptions.RecordOnly`；临时文件与最终成品均需用户显式选择。
- 启动仍只把运行态迁移为“已中断”并核对磁盘事实，不自动发起下载。
- 文件名使用 Windows/Linux 非法字符并集，处理保留名、Unicode 代理对截断与目录过长。

## 5. 自动化验收证据

执行命令：

```text
dotnet build Plugins/BiliDownloader/BiliDownloader/BiliDownloader.csproj --no-restore
dotnet test Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj --no-restore
```

结果：0 错误、0 警告；333/333 插件测试通过，宿主 UI 测试 23/23 通过。新增验收覆盖高频快照、并发 Flush、重复 Shutdown、分块恢复、日期筛选、完整预设往返、命名冲突、不可变提交边界、未知 Document 主版本、缺失预设/画质回退和并发显式初始化。原有 323 项测试经新安全语义适配后全部保留通过。

## 6. 视觉与人工复核边界

XAML 编译已验证 0 警告；Avalonia Headless 测试已覆盖 320/479/480/640/700px、浅色/深色、480px class 切换和虚拟化面板。代码包含加载/无任务/无结果/错误/批量态、键盘可聚焦控件与 Automation 名称。最终像素截图与真实字体/系统缩放观感仍属于发布前人工视觉复核，不在无窗口单元测试中伪造“已截图通过”的证据。
