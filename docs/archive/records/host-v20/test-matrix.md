# V20 测试去向与开发验收矩阵

> 基线：`20e8121`（V19 代码与已确认的 V20 方案）；2026-09-21。先建立本表，再迁移测试。执行结果见同目录 `development-evidence.json`，不以测试总数代替行为覆盖。

## 1. 逐方法迁移

以下短名称沿用原测试方法；未特别标注时保留原方法名及参数。P1 首先在保留旧生产类型的条件下验证 V3 行为迁移，再删除旧依赖。

| 原位置 / 方法与参数 | 原断言与去向 | 当前落点 / 矩阵 |
| --- | --- | --- |
| DockFourWayLayoutTests.运行时校验器稳定返回激活Pane和ToolDock错误码 | 删除。三个错误码约束旧 V2 固定 Pane/ToolDock 和全量拒绝恢复，不属于 V3 投影政策 | A01；现行结构由 V3Validator，缺失项由 AvailabilityTests 覆盖 |
| V2Tool方向映射到对应Dock（Left/Right/Top/Bottom） | 保留四向工具元数据映射，去掉误导的 V2 名称 | Tool方向映射到对应Dock / B01 |
| HiddenBottomToolCanBeRestoredAfterLayoutRestart | 迁到 LayoutState Capture/Apply；隐藏记录、原工具恢复、可见归属和空 Pane 展开不丢失；V3 不要求预先创建空 BottomTools | 同名 / B03 |
| RuntimeVerticalSplitImmediatelyUsesFullWidthStableDockAndRestores（Top、Bottom） | 运行时稳定全宽分割断言保留；恢复检查 V3 上下关系、比例、工具归属和引用身份，不要求旧固定别名 | 同名 / B01 |
| PinnedToolRoundTripsAsCollapsedEdgeTab（四方向） | V3 autoHidden、边栏方向、未进入隐藏集合/可见树、再次捕获保持状态 | 同名 / B02 |
| ExpandedPinnedAndHiddenToolsPreserveDistinctStatesAndPinnedOrder | 保留三种状态、两个 pinned 的顺序及恢复后的实例归属 | 同名 / B02 |
| FourWayLayoutPlacesTopAndBottomAcrossFullWorkspaceWidth；EmptyVerticalAlignmentsDoNotCreateBlankWorkspaceRows；HidingLastTopToolCollapsesPaneAndRestoreExpandsIt；HiddenToolRestoresAfterItsEntirePaneWasRemoved（四方向） | 现行运行时几何与同会话实例断言不依赖旧格式，原样保留 | 同名 / B01、B03 |
| DockLayoutAvailabilityTests.生命周期未就绪的工具不投影但保留可见意图和原V2字节 | 改用真实 V3 Store 与 Lifecycle；保留不投影、可见意图及比例；移除旧输入字节断言 | 生命周期未就绪的工具不投影但保留V3可见意图 / B04 |
| 未注册工具保留在V3且不隔离合法V2输入 | 直接写 V3，验证没有实例、ID/状态/比例保持、无隔离和错误诊断 | 未注册工具不创建实例且保留V3恢复记录 / B04 |
| AutoHideRestoreVisualTests.重启后边栏工具展开及固定均有可见内容（四方向 × keepExpandedTool=false/true） | 直接 V3 输入；八组真实 UI 的两轮 preview/fixed、最小内容尺寸、方向、活动项、View/Model 身份断言保留 | 同名 / B02 |
| ToolCenterUiTests.旧布局定向迁移保留有效工具且新增工具默认隐藏并可全隐藏重启 | 删除退役布局输入，保留有效工具自动收起、新工具默认隐藏、全隐藏写入并重启、重新打开 | V3恢复可见工具且新增工具默认隐藏并可全隐藏重启 / B02、B06 |
| DockLayoutV3Tests.V2转换保留比例顺序隐藏与自动隐藏且不修改输入 | 删除纯转换契约；比例和状态已在上述真实 V3 恢复用例保留 | B01、B02、A01 |
| DockLayoutV3StoreTests.V2只读转换并保留V1和V2原始字节（invalid=false/true） | 行为明确改变；迁为 valid/broken/locked 旧输入忽略测试，检查实际 Lifecycle 默认全隐藏及 V3 保存 | 旧布局不参与恢复或诊断且仍可保存V3（valid/broken/locked） / L01、L02 |
| 双坏或只剩隔离历史时不会重新导入过期V2 | 保留双坏/隔离历史断言，增加默认保存后的 V3 验证 | 同名 / L04 |
| HostDataRootPolicyTests.V2存储不读取改写迁移或删除V1目录 | 替换为 V3 Store，仍验证 v2 数据根与 v1 数据隔离，旧文件原样保留 | 现行存储不读取改写迁移或删除V1目录 / L10 |
| V3可以读取既有V2文档与布局且不会改写源文件 | Document Envelope V2 读取保持；布局改为 V3 输入，文件只读不改写仍验证 | 现行存储读取V2文档与V3布局且不会改写源文件 / L10 |
| VersionPolicyTests 的 schema/文件名断言 | 只把 Layout 的 2/layout-v2 改为 3/layout-v3，数据根、SDK、manifest、Document 独立版本保持 | 原版本测试 / L10 |
| PluginStatusMigrationTests.插件状态在隐藏停靠和自动收起状态下均可退役并保留其他布局（false,false / true,false / true,true） | 删除 V2 迁移及 pre-tool-retirement 备份契约，不迁入 V3 | A01 |
| 仅退役项可以迁移为空而其他未知项和活动工具引用仍保留 | 删除 V2 转换器测试；当前未知项保留由 AvailabilityTests 和 DockLayoutTreeTests 覆盖 | A01、B04 |
| PluginStatusMigrationTests.历史收藏最近分类与名称一起迁移且缺失插件偏好不会丢失 | 非 Layout 兼容能力，原样保留 | L10 |
| ToolCenterTests.旧管理项迁移幂等并在覆盖前保留原始字节（false,false / true,false / true,true） | 删除 V2 迁移与原始备份机制专属测试 | A01 |
| 仅管理项可迁移为空且其他未知工具不会被吞掉 | 删除旧转换器测试；现行缺失工具投影继续验证 | A01、B04 |
| ServiceAndModelTests.G5Descriptor中的文档都形成显式菜单分组 | 改为 Categories/EntryCount，仍断言分类一 2 项、分类二 1 项、其他分类存在 | 同名 / Q01 |
| 多入口策略展开为同一文档类型的独立菜单项 | 直接 Items.Entry，保留 DocumentTypeId 和 quick-url、personal-source 的声明顺序 | 同名 / Q02 |

## 2. 旧 DockLayoutStoreTests 的逐方法处置

| 方法与参数 | 去向与理由 |
| --- | --- |
| 合法V2快照可以原子往返且不留下临时文件 | 删除旧 Store 测试；V3StoreTests.原子更新保留上一有效布局且不残留临时文件覆盖现行 L07 |
| 已有V2布局通过原子替换更新 | 删除旧 Store 测试；同上保留有效主文件、备份及更新内容断言 |
| V1文件不读取不迁移也不隔离 | 并入新旧布局忽略测试，仍验证原字节和无隔离；L01、L02 |
| V2严格拒绝未知重复缺失浮动字段和V1版本（方法内 22 个 JSON 输入） | 删除 V2 schema 的字段与错误码契约；当前字段、重复/未知、深度和大小由 V3Tests/BoundaryTests 独立覆盖 L06 |
| 损坏Json整体隔离且日志不包含原文（注释、尾逗号、包含敏感样本的坏 JSON） | 旧“移动隔离”政策退出；补 V3 直接诊断断言，保留原位置和诊断副本，安全错误消息不带输入；L03、L06 |
| 重复顺序非法比例隐藏Pinned和错误活动项均被拒绝 | V2 的 order、两个布尔值和全局 active 模型退出；现行树活动项、状态、比例已有 V3 验证，不能保留旧结构规则 |
| V2结构校验覆盖空对象版本集合和全部Id重复边界 | 旧模型校验退出；现行 V3 结构及资源边界继续测试 |
| 快照模型只包含V2结构字段和原生布尔状态 | V2 专属模型退出；现行 V3 往返测试继续保证不存 Document 内容 |
| 严格Json编解码器拒绝空流和空快照参数 | 删除退出 API 的参数契约，V3 reader/writer 未改动 |

## 3. 现行行为与工具矩阵

| 矩阵 | 真实落点与审查要求 |
| --- | --- |
| L01–L05、L07–L08 | DockLayoutV3StoreTests；真实生命周期忽略旧输入、主备恢复、未来格式、写锁、事务失败 |
| L06 | DockLayoutV3Tests、DockLayoutV3BoundaryTests；异常归位保持 code/stableId，诊断不回显原文 |
| L09 | DockLayoutSaveQueueTests；UiTests 的最终写入失败/取消/退出/重启 |
| L10 | HostDataRootPolicyTests、VersionPolicyTests、DocumentEnvelopeV2Tests、API 比较及差异审查 |
| B01–B04 | 上表迁移方法及 DockLayoutWorkspaceStateTests、DockLayoutTreeTests |
| B05–B06 | DockLayoutV3UiTests、DockToolWindowCloseUiTests、DockToolSplitUiTests、DocumentWindowV16UiTests、HostRestartUiTests；关闭否决、失败回滚、原 Scope/View、退出重启 |
| W01–W04 | Verify-LayoutV3WriterLease.ps1；专属持锁子进程、就绪信号、跨进程只读与新会话写入、路径检查和 finally 回收 |
| G01–G04 | Gate 的共享产物检查单测及原有 Profile 图测试；只调用文件检查，不启动 Windows Smoke；实际 Smoke 留待发布 |
| Q01–Q03 | ServiceAndModelTests、PluginNavigationTests、CognitiveUxV8Tests、CommandPaletteV18Tests 及对应 UI；旧方法引用检索 |
| A01–A03 | 删除清单、共享异常、RetiredHostToolIds 在 V3 Merge/偏好中的真实消费者；所有权、依赖、中文注释差异审查 |
| D01–D03 | 文档链接、GitHub 锚点、实际 HelpContentCatalog 嵌入读取/渲染；八章节与理论入口保持 |

最终记录将把以上方法与实际 TRX/命令关联。纯兼容测试退出允许数量下降；失败和重跑保留在证据中，不掩盖迁移错误。
