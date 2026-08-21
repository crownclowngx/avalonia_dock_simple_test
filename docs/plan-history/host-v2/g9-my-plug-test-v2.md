# G9：MyPlugTest 迁移至 Host V2

> 完成日期：2026-08-21<br>
> 状态：已完成<br>
> 阶段性质：开发期非发布迁移；`publishable=false`

## 1. 目标与边界

G9 将 MyPlugTest 作为首个真实业务插件完整迁移到最终 Core/UI SDK、manifest v2、每插件独立 Provider、声明式贡献、普通模型与 Host Dock Adapter。既有 4 个 Document、1 个 Tool、网络、Excel、消息和保存行为保持不变，不增加示例功能，不修改 Host/SDK public API 或版本。

DaTangAccountingHelpPlug、MySmallTools、BiliDownloader 未提前迁移，仍由不可打包的 Legacy 阶段桥支持源码回归，分别留给 G10–G12。G9 不读取 MyPlugTest V1 内容、旧插件包、GUID、旧 Tool ID 或 `LegacyIds`。

## 2. SOLID 与朴素设计取舍

| 原则 | G9 落点 |
| --- | --- |
| 单一职责 | 模块只做组合；Descriptor 只描述贡献；Codec 只处理 Welcome 内容；Host 独占路径与保存事务；订阅者独占订阅令牌 |
| 开闭原则 | 复用既有 `AddDocument`、`AddPersistableDocument`、`AddTool`，没有为 MyPlugTest 增加 Host 分支或 public API |
| 里氏替换 | 4 个 Document 只实现最终 `IPluginDocument` 语义；持久化能力仅由 Welcome 通过 `IPersistablePluginDocument` 显式增加 |
| 接口隔离 | Tool 是普通 `ObservableObject`，不承担标题、关闭、停靠或浮动接口；Document 只观察窄 `IDocumentLifetime` |
| 依赖倒置 | 模型通过构造注入 Core SDK 端口与插件业务端口；不使用静态 ServiceProvider、服务定位或测试双接口 shim |

只采用三种朴素结构：构造注入表达必需依赖，Host Adapter 隔离 Dock，Codec 隔离严格 JSON。没有引入抽象工厂、策略层、消息基类或额外生命周期框架。

## 3. 声明式贡献矩阵

`MyPlugTestPluginModule.Configure` 是模型、View 与 Descriptor 的唯一事实源。注册方法自动建立 Document scoped 和 Tool singleton，模块不重复注册贡献模型，也不单独注册 View。

| 类型 | 稳定 ID | 模型 / View | 元数据与生命周期 |
| --- | --- | --- | --- |
| Persistable Document | `myavalonia.plugin.my-plug-test.document.welcome` | `TestWelcomeViewModel` / `TestWelcomeView` | 欢迎；测试插件；每实例 Scope |
| Document | `myavalonia.plugin.my-plug-test.document.message-receiver` | `TestMessageReceiveViewModel` / `TestMessageReceiveView` | 测试消息订阅组件；每实例 Scope |
| Document | `myavalonia.plugin.my-plug-test.document.batch-http-get` | `BatchHttpGetViewModel` / `BatchHttpGetView` | 逐行 HTTP GET；每实例 Scope |
| Document | `myavalonia.plugin.my-plug-test.document.excel-get-url-generator` | `ExcelGetUrlGeneratorViewModel` / `ExcelGetUrlGeneratorView` | Excel GET 地址生成器；每实例 Scope |
| Tool | `myavalonia.plugin.my-plug-test.tool.custom` | `MyCustomToolViewModel` / `MyCustomToolView` | 默认 Right；关闭 Hide；插件级 singleton |

插件项目显式引用 Core 与 UI SDK，二者均 `Private=false`。Legacy、Dock 与 Newtonsoft 依赖已删除；构建协议的 `ManagedPluginUseV2EntryContract=true` 让入口探针直接验证最终 UI SDK `IPluginModule`。另外三个未迁移插件仍使用原有 Legacy 探针，不形成双运行入口。

## 4. Welcome 内容 schema

内容 schema 固定为 `1`，payload 唯一结构为：

```json
{
  "url": "https://example.test/",
  "responseContent": "body",
  "historyItems": [
    { "url": "https://example.test/" }
  ]
}
```

`TestWelcomeDocumentContentCodec` 使用 System.Text.Json 原生 `JsonElement`。它严格拒绝：错误 schema、非对象根、未知/重复/缺失字段、错误字段类型、`historyItems` 非数组、数组项非对象以及历史项内部未知/重复/缺失 `url`。编码结果和 `DocumentContent` 都克隆 JSON，调用方释放临时 `JsonDocument` 后内容仍有效。

恢复采用“验证后提交”：Codec 先把全部字段解码到独立临时状态，只有结构和类型全部通过后，ViewModel 才一次替换 URL、响应和历史列表并清除脏状态。任何失败都不部分修改现有模型。

## 5. 所有权与释放顺序

1. Host 为每次 Document 激活创建独立 Scope，并在 Scope 中提供必需的 `IDocumentLifetime`。
2. Activator 创建模型，调用 `InitializeAsync`，创建无参 View 并设置 `DataContext`；全部成功后才发布 Adapter。
3. 用户确认关闭后，Host 先取消 `IDocumentLifetime.ClosingToken`，网络/Excel命令据此协作停止。
4. Host 释放 View/Adapter，再释放 Document Scope；模型的 `Dispose` 释放命令取消源和事件订阅令牌。
5. 一个消息接收 Document 只拥有自己的令牌，关闭它不影响同类型其他订阅者。后台发布时，接收模型显式切回 Avalonia UI Dispatcher。
6. Tool 模型由插件 Provider 独占并保持 singleton；关闭仅隐藏 Adapter，恢复不重建模型。插件关闭时再释放 Tool 与 Provider。

事件 DTO `RequestResponseMessage` 是插件自有普通密封 record，不继承 Toolkit 消息类型。发送方只依赖 `Publish`，接收方承担订阅和 UI 调度职责。

## 6. 失败矩阵

| 失败点 | 对外结果 | 不得发生 |
| --- | --- | --- |
| Descriptor/Provider 组合失败 | MyPlugTest 整体不进入 Registry | 发布部分 Document 或 Tool |
| Document 构造或初始化失败 | 暂存 Scope 释放，标签不发布 | 遗留 View、Adapter 或订阅 |
| View 创建失败 | 模型与 Scope 释放 | 发布空 DataContext View |
| Welcome schema/字段/type 损坏 | 恢复失败并保留原模型状态 | 部分覆盖 URL、响应或历史 |
| 关闭期间 HTTP/Excel 仍在途 | ClosingToken 协作取消，随后 Scope 释放 | 跨 Scope 取消其他 Document |
| 一个订阅者释放 | 仅该令牌解绑 | 清空总线或影响其他订阅者 |
| 最终 ZIP 携带共享程序集 | 静态包边界门禁失败 | 依赖加载顺序掩盖类型身份错误 |

## 7. 自动化测试与门禁证据

G9 专项入口：

```powershell
.\scripts\Test-MyPlugTestV2.ps1 -Configuration Release
```

专项实际通过 **86/86**：Plugin 59、Headless UI 14、Plugin SDK 12、解压后最终 ZIP 真实加载 1。覆盖内容包括：

- 4 个 Document Descriptor、1 个 Tool Descriptor 的 ID、模型/View 类型与元数据；
- 真实 `PluginProviderOwner`、Registry、Activator 与 Host Dock Adapter 组合；
- 多 Document Scope/局部历史隔离、默认与指定标题、关闭局部释放；
- Tool singleton、隐藏恢复、5 个 View 的 `DataContext` 与 Headless UI 绑定；
- 精确事件类型、多订阅者、独立释放及后台投递切回 UI；
- Welcome 捕获、恢复、脏状态、提交、取消、克隆和损坏 payload 负例；
- 网络/Excel 既有取消、文件交互与业务回归；
- 生产源码与最终 ZIP 的 Legacy、Dock、Newtonsoft、Host/SDK 共享依赖扫描。

脚本建立两个隔离测试 ZIP。两者均为 11 个文件，排序清单、长度、逐文件 SHA-256 和归档 SHA-256 完全一致；解压后通过真实 `PluginLoadContext`、模块预检、插件 Provider 组合并形成 4 Document + 1 Tool Registry。机器摘要位于 `artifacts/test-results/MyPlugTestV2/summary.json`，固定记录：

```json
{
  "aiflow": false,
  "windowsCi": false,
  "windowsSmoke": false,
  "releaseAcceptance": false,
  "releaseGate": false,
  "publishable": false
}
```

全量回归实际通过：locked restore；Release `-warnaserror` 全解决方案零警告构建；Host Unit 172、
UI 46、Plugin 195，共 **413/413**；Host 行覆盖率 **83.24%**、分支覆盖率 **68.83%**，高于
G8 的 83.05%/68.65% 下限；SDK 单元 **32/32** 及 Core/UI API、隔离 nupkg 消费门禁；
BiliDownloader 719、DaTangAccountingHelpPlug 64、MySmallTools 183，共 **966/966**；四插件两轮
非发布包矩阵；文档核心/完整门禁与 `git diff --check`。

明确未运行 AIFLOW、Windows CI、真实窗口 Smoke、ReleaseAcceptance、正式发布门禁、签名、上传、标签或发布操作；两个 ZIP 只用于 G9 加载验证，不可发布。

## 8. 回滚边界

回滚单位是整个 G9：MyPlugTest 生产代码、测试、构建入口探针、专项脚本、快速开始和当前事实文档一起回退。Host V2、manifest v2、Document/Layout V2 与 G0–G8 不回退，也不增加加载 MyPlugTest V1 包或读取 V1 payload 的兼容 shim。回滚后 MyPlugTest 与另外三个旧插件一样被 V2 预检隔离，直至重新完成迁移。
