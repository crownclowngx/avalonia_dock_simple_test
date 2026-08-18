# MyAvaloniaManagement Plugin SDK

该包是 Managed Plugin v1 的基础编译契约，程序集名称保持为
`MyAvaloniaManagementCommon`。它提供插件身份、显式贡献注册、Document、Tool、生命周期、消息和保存接口，
不包含宿主可执行程序集、全局主题或第三方 UI 控件实现。

普通插件只引用本包，并通过宿主提供的 `App*` 语义资源适配浅色与深色主题。需要直接使用
Semi、Ursa 或 Dock UI 控件的插件应改用同版本的
`MyAvaloniaManagement.PluginSdk.UI`。插件交付目录不得包含本包或宿主共享 UI 程序集的副本。

当前包仅作为仓库和正式发布流水线的可验证制品，不会自动发布到公共 NuGet 源。
仓库尚未选择对外许可证；对外分发前必须由项目所有者补充许可证并完成发布评审。

每个入口程序集只提供一个 public 无参 `IPluginModule`，并在组合阶段实现
`Configure(IPluginRegistrationContext)`。manifest 是插件身份唯一事实源，模块通过只读
`context.PluginId` 取得宿主已验证的身份。`context.Services` 只注册插件私有业务服务；Document、
Tool、动态 View 和可选 Lifecycle 必须分别使用 `AddDocument`、`AddTool`、`AddView` 和
`AddLifecycle`。宿主不会扫描程序集或根据类型名称推断未登记贡献，也不支持运行期追加、移除或热卸载。

Document 策略和 Tool 策略由根容器创建；Lifecycle 为每插件至多一个根级实例；View 使用登记时的
无参工厂按需创建，运行依赖应放入 ViewModel。注册只在根容器建立前发生，任何配置、激活或全量
校验失败都会放弃整个组合结果，不会发布部分 Registry。

可保存 Document 通过 `new DocumentSaveData(contentSchemaVersion, payload)` 只交付不可变业务内容。
内容版本必须为正整数，payload 不得为 `null`，其业务有效性由插件校验。磁盘信封中的插件身份、
Document 类型、标题、UTC 保存时间和宿主 schema 全部由 Host 拥有；插件不得在 payload 中复制并
依赖这些字段。v1 是第一个且唯一受支持的信封，不提供旧字段探测或迁移。

完整用法见仓库 `docs/quick-start/create-managed-plugin.md` 和
`Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md`。
