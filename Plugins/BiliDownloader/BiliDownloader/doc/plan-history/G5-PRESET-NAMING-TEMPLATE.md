# G5：下载预设与命名模板

> 2026-08-02 复核更新：基线 `615badc` 的预览未接通、非法/重复名称可提交、构造函数 fire-and-forget 初始化及 Document V2 恢复不完整问题已修复。下载配置使用完整不可变 `DownloadProfile`，预设支持复制、命名、重命名和删除；Document 配置优先于全局默认且画质可延迟恢复；命名预览随解析、选择和标题变化刷新，变量可点击插入，非法模板、空结果及批内重名会阻止提交。

> 实施日期：2026-08-02
>
> 状态：已完成
>
> 平台范围：Windows、Linux；与 G4 相同

## 1. 完成目标

G5 解决的是下载配置从"每次手动设置"到"一键复用方案"的产品化问题：

- 提供"兼容""质量""归档"三个内置预设，覆盖最常见下载场景。
- 支持复制内置预设并保存为自定义预设。
- 记忆最后使用的预设和输出目录。
- 支持 `{title}`、`{index}`、`{bv}`、`{up}`、`{date}`、`{series}` 命名变量。
- 提供命名验证和所选内容前 3 项实时预览。
- 将 Document 保存格式升级为 V2，完整保存 P0 下载规则。
- 旧 V1 Document 可以无损打开并补齐默认值。
- 不会生成 Windows 非法文件名、保留名或超长路径。

## 2. 关键决策

### 2.1 命名解析在 Document 侧完成（SRP + 稳定性）

命名模板在 `VideoListViewModel.SubmitDownload()` 中渲染为最终标题，Coordinator 接收的 `DownloadItemInfo.Title` 已是成品文件名。

**为什么不放在 Coordinator 侧**：Coordinator 有 G2/G3/G4 共 1200+ 行测试覆盖，修改侵入性高。Document 侧解析保持 Coordinator 接口稳定，零回归风险。

### 2.2 预设存储复用 settings 表（最小变更）

自定义预设以 JSON 序列化后存入 `settings` 表的 KV 结构（key = `preset:{id}`），不新建独立表。

**为什么不用独立 presets 表**：预设数量极少（内置 3 + 自定义 < 20），KV 存储足够。独立表需要数据库迁移脚本，变更足迹大。内置预设始终从代码获取（`BuiltInPresets.GetAll()`），不写入数据库，避免污染。

### 2.3 命名模板引擎为纯函数静态类（可测试性）

`NamingTemplateEngine` 与 G4 的 `TaskFilterSortEngine` 设计一致：无状态静态类，无 DI 依赖。

**为什么不用正则表达式**：变量数量固定（6 个），简单字符串扫描 O(n) 线性、零额外分配、可读性更高。正则在这个场景下是过度设计。

### 2.4 FileNameSanitizer 独立提取（SRP）

将 `BiliDownloadService.SanitizeFileName` 提取为独立的 `FileNameSanitizer` 静态类，增强处理 Windows 保留名、尾部点号/空格、路径超长。

**为什么增强而非复用**：原实现仅替换非法字符，不处理 CON/NUL/COM1 等保留名，不校验路径总长度。退出条件"不会生成 Windows 非法文件名"需要完整实现。

### 2.5 Document V2 强类型 DTO（可读性）

使用 `DocumentSaveDataV2` 强类型类替代 V1 的匿名对象。

**为什么不用 JObject 手动解析**：强类型 DTO 提供编译期检查、IDE 自动完成和清晰的数据契约。V1 兼容通过 `NullValueHandling.Ignore` + 字段默认值实现。

### 2.6 NamingTemplateViewModel 与 RenamePanelViewModel 并列（SRP）

新建 `NamingTemplateViewModel` 管理模板编辑/验证/预览，不修改 `RenamePanelViewModel`。

**为什么不合并**：手动重命名（逐行编辑）和模板命名（自动渲染）是两种独立功能，职责不同。合并会违反 SRP，且增加测试复杂度。手动重命名优先级高于模板（`IsRenamed` 的项不使用模板）。

## 3. 代码边界

| 文件 | 职责 |
|------|------|
| `Models/DownloadPreset.cs` | 预设数据模型（record）+ BuiltInPresets 静态工厂 |
| `Models/DocumentSaveDataV2.cs` | Document V2 强类型 DTO |
| `Models/BiliVideoCollection.cs` | +UpName +PublishDate（命名变量数据源） |
| `Services/Naming/FileNameSanitizer.cs` | 文件名安全器（非法字符/保留名/路径长度） |
| `Services/Naming/NamingTemplateEngine.cs` | 命名模板引擎（渲染/验证/预览）+ NamingContext |
| `Services/Persistence/IPresetRepository.cs` | 预设仓储接口 |
| `Services/Persistence/PresetStore.cs` | 预设持久化（复用 settings 表 KV） |
| `ViewModels/BiliDownloader/NamingTemplateViewModel.cs` | 命名模板编辑/验证/预览 VM |
| `ViewModels/BiliDownloader/DownloadConfigViewModel.cs` | +预设列表/选中/应用/保存为预设 |
| `ViewModels/BiliDownloader/VideoListViewModel.cs` | SubmitContext 扩展 + 模板渲染替换硬拼接 |
| `ViewModels/BiliDownloaderViewModel.cs` | Document V2 保存/加载 + NamingTemplate 子 VM |
| `Services/Api/BiliApiService.cs` | 提取 owner.name 和 pubdate |
| `Plugin/BiliDownloaderPluginModule.cs` | +IPresetRepository DI 注册 |

## 4. 预设应用流程

```
用户选择预设 → ApplyPresetCommand
    │
    ├─ ApplyPreset(preset)
    │     ├─ UseGroupFolder = preset.UseGroupFolder
    │     ├─ AddIndexToTitle = preset.AddIndexToTitle
    │     ├─ DownloadDanmaku/Subtitle/Cover = preset.xxx
    │     ├─ OutputDirectory = preset.OutputDirectory（若非空）
    │     └─ _pendingQualityPreference = preset.QualityPreference
    │
    ├─ 记忆 last_preset_id → ISettingsRepository
    │
    └─ 清晰度延迟匹配（PopulateQualities 时）
          └─ MatchQualityByPreference(qualities, preference)
                ├─ "720p" → QualityId=64 或回退
                ├─ "1080p" → QualityId=80 或回退
                └─ "highest" → 最高 QualityId
```

## 5. 命名模板渲染流程

```
用户编辑模板 → NamingTemplateViewModel.Template
    │
    ├─ OnTemplateChanged → RefreshValidationAndPreview()
    │     ├─ NamingTemplateEngine.Validate(template)
    │     │     ├─ 空模板 → 错误
    │     │     ├─ 未闭合花括号 → 错误
    │     │     └─ 未知变量 → 错误 + UnknownVariables
    │     │
    │     └─ NamingTemplateEngine.Preview(template, contexts[0..3])
    │           └─ 渲染前 3 项 → PreviewItems
    │
提交下载 → VideoListViewModel.SubmitDownload()
    │
    ├─ foreach item in selectedItems:
    │     ├─ item.IsRenamed → 直接使用 item.Title（手动重命名优先）
    │     └─ 否则 → NamingTemplateEngine.Render(template, context)
    │           ├─ 逐字符扫描 {variable} 占位符
    │           ├─ ResolveVariable → 上下文值
    │           └─ FileNameSanitizer.Sanitize(result)
    │
    └─ 构造 SubmitDownloadTaskMessage → Coordinator
```

## 6. Document V2 迁移流程

```
保存 Document → CreateSaveDocumentMetaData()
    │
    ├─ 构造 DocumentSaveDataV2（强类型 DTO）
    ├─ PluginMetadata.Version = "2.0"
    └─ JsonConvert.SerializeObject(dto)

加载 Document → LoadDocumentByMetaData()
    │
    ├─ 解析 PluginMetadata.Version
    │
    ├─ "1.0" → LoadV1()
    │     ├─ JObject 逐字段读取（保持原有逻辑）
    │     └─ 补齐默认值：
    │           ├─ AddIndexToTitle=true → NamingTemplate="{index}.{title}"
    │           └─ AddIndexToTitle=false → NamingTemplate="{title}"
    │
    ├─ "2.0" → LoadV2()
    │     └─ JsonConvert.DeserializeObject<DocumentSaveDataV2>()
    │           └─ 完整恢复所有配置
    │
    └─ 未知版本 → 日志警告 + 回退 LoadV1()（向前兼容）
```

## 7. 自动化验证

| 测试文件 | 数量 | 覆盖范围 |
|---------|------|---------|
| `NamingTemplateG5Tests.cs`（FileNameSanitizer） | 12 | 非法字符/保留名/尾部点号/空输入/超长路径/中文标题 |
| `NamingTemplateG5Tests.cs`（Render） | 12 | 各变量渲染/多变量组合/空值/特殊字符/100项性能 |
| `NamingTemplateG5Tests.cs`（Validate） | 6 | 合法/未知变量/未闭合/空模板/仅空白 |
| `NamingTemplateG5Tests.cs`（Preview） | 4 | 前3项/不足3项/空列表/经过Sanitize |
| `NamingTemplateG5Tests.cs`（DownloadPreset） | 6 | 三个内置预设/record相等性/自定义复制 |
| `NamingTemplateG5Tests.cs`（PresetStore） | 6 | CRUD往返/内置拒绝删除/拒绝覆盖/重复保存 |
| `DocumentV2G5Tests.cs`（V2往返） | 4 | 版本正确/新增字段/模板往返/附加资源往返 |
| `DocumentV2G5Tests.cs`（V1兼容） | 3 | 补齐默认值/AddIndexToTitle迁移/原有字段不丢失 |
| `DocumentV2G5Tests.cs`（版本判别） | 4 | 未知版本/空Metadata/空Content/null Content |
| `DocumentV2G5Tests.cs`（模型） | 2 | 默认值/缺失字段反序列化 |
| 现有测试回归 | 256 | 全部通过，无破坏性变更 |
| `AggressiveRefactorAcceptanceTests.cs` | 10 | G3–G5 闭环、缺失预设/画质回退与新提交边界 |
| **总计** | **333** | |

## 8. 退出条件验证

| 退出条件 | 验证方式 |
|----------|----------|
| 新建 Document 可以一键应用预设 | DownloadConfigViewModel.ApplyPresetCommand + 预设列表绑定 |
| 保存并重新打开 Document 后配置保持一致 | DocumentV2G5Tests：V2保存加载往返一致测试 |
| 旧 V1 Document 可以无损打开并补齐默认值 | DocumentV2G5Tests：V1兼容加载测试（3项） |
| 不会生成 Windows 非法文件名、保留名或超长路径 | NamingTemplateG5Tests：FileNameSanitizer 12项测试 |

## 9. 明确限制与后续工作

- 预设的 `QualityPreference` 为字符串（"highest"/"1080p"/"720p"），P1 阶段增加编码/容器选择时需扩展为更复杂的策略对象。
- `{up}` 和 `{date}` 变量在番剧场景下为空（番剧无 UP 主概念，API 不返回 pubdate）。渲染结果为空串，经 Sanitize 后回退为 "download"。
- 路径超长截断后追加 MD5 前 6 位哈希保证唯一性，但极端情况下仍有理论碰撞可能。G6 冲突预检阶段会进一步兜底。
- 预设 JSON 格式未来变更时，`NullValueHandling.Ignore` + 字段默认值可保证向前兼容，但不支持字段重命名。
- 所有生产与测试调用点均已迁移到 `FileNameSanitizer.Sanitize`，过时兼容入口不再产生编译警告。
- 剩余时间估算已使用持久化的数值速度计算，不再解析 UI 展示字符串作为主数据源。
