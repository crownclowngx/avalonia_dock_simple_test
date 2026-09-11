# MyAvaloniaManagement V6.1 公共图标资源包与插件专属图标改造方案

> 状态：实现、验证与 NuGet 发布完成；实际结果见 [专属实施记录](../plan-history/host-v6.1/icon-contributions-and-resources-acceptance.md)。
> 日期：2026-09-11；代码核查基线：`cc41a2e` 加当前工作树。
> 开始时有 17 个 lock 文件修改；实现阶段先备份原始输入，再随 SDK/依赖更新锁文件，详见实施记录。
> 承接：[V6 插件目录与功能中心](./host-v6-plugin-navigation-and-function-center-plan.md)。
> V6.1 表示本次改造的时间顺序；方案副标题可以自定义，不直接等于产品或 NuGet 版本。
> 用户已认可“插件声明、Host 管理、SDK 提供契约”的流程和边界，并明确要求同时交付公共资源包与插件专属图标能力。
> 实施遵循 SOLID、朴素设计和中文设计注释；不使用 AIFLOW、Windows CI、Windows Smoke 或发布 seal。

## 1. 目标与明确决策

把 V6 的固定 Host 图标表演进成可以扩展的图标贡献机制，并建立独立的公共矢量资源 NuGet 包。
公共图标与专属图标并存，所有 Host 展示入口通过同一解析链显示图标，缺失时继续使用默认四宫格。

| 部分 | V6.1 决策 |
| --- | --- |
| SDK 契约 | `PluginSdk.UI` 增加不可变矢量描述、可选图标注册能力与扩展方法 |
| 公共资源 | 新建 `MyAvaloniaManagement.Icons`，保存可复用矢量数据与稳定名称，独立维护包版本 |
| 插件专属图标 | 插件在模块注册阶段声明，Host 自动绑定真实插件身份；图形可以自带或取自公共资源包 |
| Host 管理 | 负责注册结果、身份、查找、可用性、缓存与默认回退，不集中维护所有业务图形 |
| 引用字段 | 继续使用现有 `IconPath` 字符串，不强制替换 Document/Tool/Command 描述符的已有字段 |
| 图形格式 | 第一版支持单色填充矢量路径、逻辑画布尺寸和填充规则 |
| 使用位置 | V6 新树、功能中心接入；公共资源同时可供插件自己的页面和 Standalone 使用 |
| 原有旧版 Tool | 保持统一四宫格的既有逻辑；默认图形的数据来源改为公共资源包 |
| 注册时机 | 模块 Configure 内同步声明，模块返回后封闭，校验后一次性提交 |
| 更新 | 新增业务图标可随插件更新；公共资源包更新由引用它的 Host/插件重新构建交付 |

公共资源包和专属注册均为本轮必做项，不能只把现有字典移动到另一个项目就宣布完成。
本轮不做图标市场、在线下载、热更新、运行中随意修改注册表、多色 SVG、字体图标引擎或动态控件工厂。
既有 UI 库中适合复用的图形，可以经来源与许可确认后整理成矢量数据；不承诺自动识别所有第三方库图标。

## 2. 核查到的当前事实

| 代码位置 | 当前事实 | 改造含义 |
| --- | --- | --- |
| [HostIconCatalog.cs](../../Host/MyAvaloniaManagement/Business/Presentation/Icons/HostIconCatalog.cs) | 固定 8 个 `builtin:` 路径，未知键统一默认，同文件 Converter 解析并缓存 Geometry | 拆开资源内容、目录查询与 UI 缓存；保留既有名称 |
| [ContributionDescriptors.cs](../../Host/MyAvaloniaManagement.PluginSdk.UI/ContributionDescriptors.cs) | Document、创建意图、Tool 已有 IconPath | 可以增加图标贡献，无需重做这些描述符 |
| [WorkbenchCommandDescriptors.cs](../../Host/MyAvaloniaManagement.PluginSdk.UI/WorkbenchCommandDescriptors.cs) | Command 也有 IconPath | 协议可复用；不因本轮顺带重做所有命令菜单样式 |
| [PluginRegistrationContracts.cs](../../Host/MyAvaloniaManagement.PluginSdk.UI/PluginRegistrationContracts.cs) | 模块只在组合阶段声明，注册入口随后封闭 | 图标沿用同一注册窗口，不另建初始化生命周期 |
| [WorkbenchCommandRegistrationContracts.cs](../../Host/MyAvaloniaManagement.PluginSdk.UI/WorkbenchCommandRegistrationContracts.cs) | 通过独立可选能力和扩展方法兼容新增注册面 | 图标沿用此模式，不向已发布 IPluginRegistration 强加抽象成员 |
| [PluginRegistrationContext.cs](../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginRegistrationContext.cs) | Host 的 internal PluginRegistration 绑定已验证 PluginId | 图标所有者从当前上下文取得，插件不能任意指定他人身份 |
| [PluginRegistryBuilder.cs](../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginRegistryBuilder.cs) | 插件候选先独立收集，成功后导入并冻结 | 图标随同一候选提交或丢弃，不产生半注册残留 |
| [PluginSharedAssemblyPolicy.cs](../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginSharedAssemblyPolicy.cs) | SDK/UI Profile 的依赖闭包使用 Host 共享实例，普通依赖保持插件隔离 | 公共图标包不能经 SDK 反向依赖进入共享闭包 |
| [当前测试说明](../reference/myavalonia-management-tests.md) | Gate 已收口到本仓 Host + MyPlugTest，外部业务组合入口已退役 | V6.1 按当前本仓门禁验收，不重建 V6 临时外部目录映射 |

本文只依据当前代码建立设计，不把 V6 历史测试数、包哈希或跨仓失败直接当作 V6.1 基线。

## 3. 包结构与依赖方向

建议新增项目 `Host/MyAvaloniaManagement.Icons/`，输出独立 NuGet 包 `MyAvaloniaManagement.Icons`。
这是资源包，名称不带 `PluginSdk`，不参与 Host 与插件之间共享类型的运行时契约。

| 项目 | 依赖 | 内容 |
| --- | --- | --- |
| `PluginSdk.UI` | 沿用现有 SDK/UI 基座 | 图标注册契约、不可变矢量描述，不引用公共图标包 |
| `MyAvaloniaManagement.Icons` | 第一版仅 .NET 基础库 | 不可变图标资产、固定名称、全部公共资产目录；不引用 SDK、Avalonia、Host、DI 或主题包 |
| Host | `PluginSdk.UI` + 公共图标包 | 导入公共目录，合并插件贡献，实现解析和 UI 绘制 |
| 普通业务插件 | `PluginSdk.UI`；按需引用公共图标包 | 专属路径定义，或从公共资产选取图形进行注册 |
| 插件 Standalone | 按需引用公共图标包和现有 UI 依赖 | 独立绘图和预览，不依赖真实 Host 注册表 |

公共包的数据对象暂称 `CommonIconAsset`，包含 Key、PathData、ViewBoxWidth、ViewBoxHeight。
提供 `CommonIcons.Table` 等具名资产和 `CommonIcons.All` 只读总目录。第一版公共资产统一使用 EvenOdd 填充。
新增公共图标时只修改资源项目；Host 枚举总目录导入，不为每个新图标增加一条手工映射。

`CommonIconAsset` 只在资源使用者内部读取，不出现在 SDK 注册方法的参数或返回值中。
调用者把它的字符串、尺寸复制成 SDK 的矢量描述再注册；这个转换是几项数据赋值，不另建适配器框架。
不可变静态资产可以复用；可写的全局静态注册表禁止放入任何包。

### 3.1 为什么资源包独立于 SDK

如果 `PluginSdk.UI` 直接引用资源包，当前共享程序集闭包可能使插件被迫使用 Host 提供的资源包版本。
插件想使用资源包新图标时，就可能再次需要先升级 Host，失去独立扩展的意义。

本方案要求资源包保持普通私有依赖：Host 可引用一个版本，不同插件可以各自引用兼容协议下的不同版本。
Host 与插件之间只传 SDK 描述中的字符串、数值和共享枚举，不传资源包对象、Type、委托、Provider 或控件。
即使名称相同的资源程序集分别加载到不同 ALC，也不会把它们的私有类型作为共享协议类型比较。

不得把资源程序集加入共享根或禁止插件携带的 SDK DLL 清单；实际 deps、ZIP 与双 ALC 测试必须证明这一点。
普通 C# AssemblyReference 的方向也必须保持正确，不能通过其他 SDK 工程间接形成反向依赖。

## 4. SDK 注册契约草案

以下为拟新增 API 的形状示意，尚不存在于已发布包，最终签名须经过 public API 基线评审。

```csharp
// 只保存路径字符串、正数画布尺寸和固定枚举；不构造 Geometry 或 Control。
public sealed class VectorIconDefinition
{
    public string PathData { get; }
    public double ViewBoxWidth { get; }
    public double ViewBoxHeight { get; }
    public IconFillRule FillRule { get; }
    // 构造器复制并检查字段；省略具体实现。
}

public enum IconFillRule { EvenOdd, NonZero }

// 独立可选能力；由绑定当前插件身份的 Host 注册对象实现。
public interface IPluginIconRegistration
{
    string AddIcon(string localName, VectorIconDefinition definition);
}

// 再为 IPluginRegistration 提供同名扩展方法，沿用已有 Command 注册风格。
// 返回由 Host 生成的完整 IconPath，避免插件自己拼写所有者前缀。
```

注册接口不接收 PluginId 参数，真实所有者来自当前 manifest 注册上下文。
扩展方法对不具备能力的 Host 给出明确的“不支持图标注册”错误；它不把失败偷偷转为注册成功。
使用该能力的新插件需要支持新契约的 Host；“找不到某个图标就默认显示”不等于可以绕过 SDK 版本兼容检查。

注册值不可变。localName 使用小写字母、数字与单个连字符分隔，首字符为字母，例如 `text-check`、`invoice-v2`。
不允许空白、冒号、斜杠或完整 `builtin:` 名称，身份比较使用序号比较并区分大小写，不做本地化转换。
画布原点固定为 `(0, 0)`，宽高必须有限且大于 0。路径字符串不能为空，填充规则必须为已定义值。
不把颜色、主题、目标显示尺寸或业务显示名固化为图标身份。

### 4.1 两种业务使用方式

下面是拟定 API 的使用示意，变量 `MyIconPaths.Invoice` 代表插件自己的矢量字符串：

```csharp
// 专属图标：数据随业务插件维护。
var invoiceIcon = registration.AddIcon(
    "invoice",
    new VectorIconDefinition(MyIconPaths.Invoice, 24, 24));

// 复用公共资源，但声明为本插件拥有的图标。
var table = CommonIcons.Table;
var reviewIcon = registration.AddIcon(
    "text-review",
    new VectorIconDefinition(table.PathData, table.ViewBoxWidth, table.ViewBoxHeight));

// DocumentDescriptor / 创建意图 / ToolDescriptor 等仍使用原有 iconPath 参数。
// 例如：iconPath: invoiceIcon 或 iconPath: reviewIcon。
```

SDK 的最终构造器应提供上述简写，默认填充规则为 EvenOdd。
插件自己的 View 也可以使用同一个 `MyIconPaths` 或公共资产绘图，避免复制图形；页面内绘图不需要向 Host 反查字符串。
若 Standalone 确实执行模块 Configure，它的注册适配必须支持新能力或使用明确的预览适配，不能提供伪成功的空注册实现。
实施时检查模板的真实启动路径，并给出“页面直接复用资源”和“模块注册预览”各自的用法。

## 5. 名称、公共资源与版本差异

只保留两类引用前缀，不额外引入一套 `common:` 别名：

| 引用 | 谁提供图形 | 可见范围 |
| --- | --- | --- |
| `builtin:table` 等 | 当前 Host 导入的公共资源包 | 所有插件可引用 |
| `plugin:<PluginId>/<localName>` | 指定插件成功提交的图标贡献 | Host 展示该插件贡献时使用 |

例如闲才插件成功注册 `text-review`，返回 `plugin:myavalonia.plugin.xiancai/text-review`。
插件 ID 仅为示例，实际身份以当前 manifest 为准。另一个插件注册相同 localName，不会与它碰撞。
第一版不支持直接引用其他插件的专属键；不同插件需要复用相同图形时共同引用公共资源包，并分别声明自己的名称。
解析专属键时需要当前贡献所有者，防止仅凭字符串让一个插件借用其他插件的身份。

### 5.1 两种复用的更新效果

| 选择 | 适用情况 | 更新影响 |
| --- | --- | --- |
| 直接填 `CommonIcons.Table.Key`，即 `builtin:table` | 希望外观跟随 Host 的公共图标版本 | Host 升级所引用资源包并重新交付后更新；旧 Host 不认识的新名称回退默认 |
| 把 `CommonIcons.Table` 的图形注册为自己的 `text-review` | 插件需要随自身交付固定图形，或使用比 Host 更新的资源 | 升级资源包并重新交付该插件即可；无需 Host 同时更新资源包 |
| 注册插件自己的路径 | 业务专属品牌/语义图形 | 只修改并交付该插件，其他插件不受影响 |

公共包新版本不会自动替换用户已安装的 DLL。NuGet 依赖升级、重新构建、交付和运行时重新加载是不同步骤，
本方案不增加在线更新或热加载机制，也不支持直接替换共享 SDK DLL 作为升级方法。

### 5.2 V6 八个名称兼容

将现有 module、folder、table、chart、text-check、image、video、download 全部迁入公共资源包，
保留 `builtin:` 名称和语义，默认仍为 `builtin:module`。迁移时通过实际渲染确认缩放、留白和镂空效果一致。
公共名称发布后不改义、不随意删除；新增名称作为兼容扩展，改画法也应保持原有功能含义。

公共目录所有者为 Host。插件不能注册或覆盖 `builtin:`；专属键不能通过名称优先级覆盖公共键。
缺失路径、空 IconPath、未知前缀、外部文件路径和 URL 继续默认回退，不读取外部资源。

## 6. 注册、提交与渲染流程

### 6.1 注册流程

1. Host 从自己引用的公共资源包导入只读公共目录。
2. 插件模块在 Configure 中通过可选接口声明专属图标；Host 生成完整键，暂存到当前插件候选 Builder。
3. 模块返回后封闭注册入口，检查字段、localName、重复键与归属；与其他贡献一起处理候选。
4. 模块配置、私有容器构建或候选提交失败，丢弃该候选全部图标；不向全局目录留下部分记录。
5. 全部候选完成后形成当前 Runtime 的不可变图标注册快照。

返回 IconPath 只表示该候选内的引用已确定，不承诺尚未结束的插件组合已经成功。
图标不依赖 Document 声明顺序；最终查找根据完整注册快照进行，不要求先声明 Document 或先打开页面。
插件没有图标声明时继续按原路径加载，不增加强制初始化工作。

### 6.2 UI 就绪后的解析流程

1. 目录投影携带最终 IconPath 及真实贡献所有者；它们是展示元数据，不改变创建入口身份。
2. 图标查询检查前缀、归属、当前插件可用性，取得只读矢量定义；未命中返回默认定义。
3. UI 渲染层在 Avalonia 就绪后解析 Geometry，并按定义身份与内容缓存；非法路径记录一次诊断并回退默认。
4. UI 为每个位置创建独立控件，使用各自的颜色和目标尺寸；只复用几何数据，不复用有父级的 Control。

贡献所有者必须从 WorkspaceCatalog/Registry 已验证的 Document、Tool 或 Command 注册关系取得，
不能把 IconPath 自报的 PluginId 当作可信所有者。V6 创建目录与树节点需要在展示投影中保留该上下文，
可用一个 internal 图标请求值组合“所有者 + 引用”，但不改变 SDK 已有 IconPath 或创建入口身份。
Host 自有贡献没有插件所有者，只允许使用公共键；解析前不猜测默认插件身份。

注册前半段不能假定 Avalonia 已经初始化，不能在模块组合期间为验证图标而创建 PathIcon 或插件 View。
注册阶段完成纯字段和身份校验；依赖绘图实现的路径语法、几何边界检查放在 UI 解析层。
公共资源包的所有路径另由独立测试加载真实绘图实现提前验证，不以“字符串非空”代替图形有效。

画布尺寸必须参与缩放和对齐。Host 可用一个轻量内部图标视图，内部由 Viewbox、固定画布与 Path 组成，
消费几何、画布宽高与填充规则；不能只绑定 Geometry 而忽略描述中的画布留白。
V6 页面只替换图标展示部分，不更改分类、搜索、选择或创建行为。

### 6.3 所有权与失效

- Host 图标注册快照和解析缓存归当前 Runtime 所有，不建立进程级可写静态单例。
- 保存纯数据，不保存插件 Provider、Model、View、Type 或回调；缓存不能把插件私有资源包对象留在 Host 中。
- 注册后不支持运行中替换图形；本轮无需引入热更新修订号或完整撤销事务。
- 插件变为不可用时，查询先按可用性过滤，再使用缓存，不能因为旧缓存命中而继续接受该专属键。
- 新树与功能中心复用已有可用性刷新，已移除的创建入口不残留；模式切换保持 V6 的独立展开状态。
- UI 解析层的订阅在相应窗口/Runtime 释放时成对退订；仍遵守 V6 的创建排空和 V5 的资源保留政策。
- 专属矢量缓存只是 Host 的图形数据，可在 Runtime 结束时统一释放；本轮不宣称实现插件热卸载。

## 7. 错误语义

| 情况 | 处理 |
| --- | --- |
| 空白 localName、非法字段、封闭后继续注册 | 明确拒绝注册调用，沿用当前模块组合失败隔离规则 |
| 同一插件重复 localName，包括内容相同的重复 | 拒绝当前候选，给出稳定诊断；不采用先到优先或后写覆盖 |
| 不同插件使用相同 localName | 合法，完整身份不同 |
| 模块失败/候选失败 | 图标随候选丢弃，其他插件不受影响 |
| IconPath 为空或找不到名称 | 公共默认图标；不影响 Document 创建 |
| 专属键归属不符/所属插件不可用 | 公共默认图标，不能绕过所有权检查 |
| 非空 PathData 无法解析、几何结果无效 | 当前图标回退默认，记录稳定错误码，不因图形坏数据删除业务功能 |
| 新 SDK 插件运行在不支持协议的旧 Host | 按 SDK/manifest 能力边界明确拒绝或提示升级，不假装可用 |

字段与身份违规属于注册错误，矢量内容无法绘制属于展示错误，两者的处理不能混为一谈。
诊断只记录稳定错误码、经过验证的所有者/图标键和异常类型，不输出完整路径正文；相同坏图标不逐帧重复打印。
默认解析不递归调用同一个失败分支；默认四宫格必须有实际绘图测试保护。

## 8. SOLID 与实现组织

| 原则 | 本方案的落实 |
| --- | --- |
| SRP | 资源包提供图形，SDK 定义契约，注册 Builder 校验贡献，查询决定可用性，UI 缓存负责绘图 |
| OCP | 新图标通过新增数据/插件声明扩展，Host 不为每个业务图形修改分支 |
| LSP | 旧插件没有图标声明仍可用，现有 IconPath 与八个 builtin 名称保持语义；新能力显式声明版本要求 |
| ISP | 独立 IPluginIconRegistration，不扩大旧 IPluginRegistration 的抽象方法要求，不暴露全局管理器 |
| DIP | 插件面向 SDK 契约，Host 在组合根显式连接具体实现；资源包不反向依赖 Host 或 SDK |

内部优先使用具体类型，建议按实际变化原因组织为：

| 建议位置/类型 | 内容 |
| --- | --- |
| `PluginSdk.UI/IconRegistrationContracts.cs` | 可选接口、扩展方法、不可变描述及固定枚举 |
| `MyAvaloniaManagement.Icons/CommonIcons.cs` | 具名资产、只读全部目录与公共名称 |
| 现有 `PluginRegistryBuilder` / `PluginRegistrationContext` | 增加图标声明收集、候选提交与封闭检查 |
| `Business/Presentation/Icons` 下的 internal 查询与缓存 | 公共/专属目录解析、可用性、默认回退、UI 几何缓存 |
| Host 内部轻量图标视图/模板 | 画布、填充规则、主题和目标尺寸绘制 |

名称是建议，不要求为每个表格行机械新增接口或独立工程。
现有 `HostIconCatalog` 的固定路径数据迁走；剩余职责按查询/渲染整理，不保留一个包装了旧静态字典的“新管理器”。
需要依赖注册表的 Converter/视图由当前 Runtime 的组合根装配，复用既有 App 资源注入方式；不在无参 Converter 中查找全局 Provider。
中文注释重点说明 ALC 数据边界、注册提交、默认回退、画布缩放和缓存所有权，避免只翻译方法名。

## 9. SDK、资源包及构建传播

V6.1 相比 V6 会增加 SDK public API，因此需要正式记录 API 兼容变化；只修改 Host 已不足以完成此需求。

| 组件 | 版本与传播策略 |
| --- | --- |
| Core/UI SDK | 沿用当前共同版本治理；已核查并发布下一兼容次版本 `3.4.0` |
| 公共图标包 | 独立版本属性，首版 `1.0.0` 已发布；不直接绑定 MyAvaloniaPluginSdkVersion |
| Host 产品 | 不因“V6.1”自动升版；交付时保证提供新增 SDK 契约 |
| Workflow SDK | 无本轮协议变化，不主动升版 |
| Build 包 | 实测发现共享库前缀规则误拦截资源 DLL，精确放行 Icons 后发布 `1.1.3`；其他共享库限制保留 |
| 模板 | 已发布 `1.4.1`，固定上述依赖并提供公共复用、专属注册与 Standalone 示例；发布过程详见实施记录 |

已有 Shipped API 文本保留，新成员进入相应 Unshipped 记录；不得修改历史基线掩盖不兼容。
新插件声明正确的最低 SDK 版本，旧插件使用已有元数据和 Host 默认图标时无需同步升级。
开发验证使用本地隔离 feed 的真实候选 nupkg、锁定还原和真实 ZIP，不能只用 ProjectReference 证明外部消费成功。
资源 DLL 作为插件普通私有依赖可以随 ZIP 交付；共享 SDK/UI DLL 仍不得混入插件包。
原方案的“不上传 NuGet”边界已由用户后续明确的编译、发布授权更新：本轮发布上述 NuGet 包，
并验证公开源消费；不创建 tag、不发布 Host 或外部插件，也不运行 Host seal。

## 10. 兼容矩阵与本轮修改范围

| 消费场景 | 期望 |
| --- | --- |
| 旧 SDK 插件 + V6.1 Host | 继续加载；旧 builtin、空图标和原创建意图语义正常 |
| 旧 SDK 插件仅引用纯数据资源包，未调用新增注册契约 | 不因资源包本身被强制升级 SDK；可以在自己的 View 使用图形，或引用旧 Host 已知的 builtin |
| 新 SDK 插件 + V6.1 Host，使用专属图标 | 按真实所有者注册并显示，创建行为不变 |
| 新 SDK 插件 + 不支持该 SDK 的旧 Host | 明确版本不兼容，不将图标默认回退误作二进制兼容 |
| 插件使用新资源包 + Host 使用旧资源包，注册为专属键 | 可显示插件随包携带的图形，SDK 协议相容即可 |
| 插件直接引用 Host 尚无的 builtin 键 | 默认图标；升级 Host 的资源包后才认识该公共键 |
| 两个插件携带不同资源包版本并注册同名局部键 | 独立解析，不共享私有资源类型或覆盖彼此 |
| 插件自己的 View / Standalone | 能直接复用公共资产和专属路径，无需 Host 全局注册表 |

实施修改范围包括 Host、UI SDK、公共资源项目、本仓 MyPlugTest/测试夹具、必要的模板示例与文档。
外部业务插件不做批量迁移；通过本仓最小独立消费夹具和真实包验证能力，后续业务插件再按需采用。
不改变 PluginId、DocumentTypeId、CreationIntentId、分类分隔规则、布局身份、导航偏好格式或 Document envelope。
现有 V6 搜索、单窗口、双模式、错误提示和创建退出保护必须完整保留。

## 11. 实施阶段

| 阶段 | 必须完成的工作 | 证据 |
| --- | --- | --- |
| G0 基线 | 记录当时源码/已有改动，复核版本与最新 Gate，冻结 SDK 草案和包依赖方向 | 基线与兼容矩阵，不借用历史测试数字 |
| G1 公共资源包 | 新建独立项目，迁移八个图形、保留键、公开只读资产目录、独立版本和 README | 真实 nupkg、资产完整性/绘图测试、依赖清单 |
| G2 SDK 与贡献注册 | 可选接口、描述符、扩展方法、候选隔离、归属、判重、封闭 | SDK API 基线、注册正反例、旧插件兼容 |
| G3 Host 接入 | 当前 Runtime 只读目录、可用性、UI 缓存、默认回退、新树/功能中心绘制 | 真实 XAML、主题与默认图标测试，V6 回归 |
| G4 独立消费 | MyPlugTest/本仓夹具展示专属图标与公共复用；不同资源版本双 ALC；模板/预览说明 | 候选 feed 锁定消费、真实 ZIP、独立页面预览 |
| G5 开发验收 | 本仓 verify、补充覆盖率与专项门禁、文档链接和资源来源检查 | 专属实施记录、实际命令/结果/限制 |

实施进展与实际证据使用
`docs/plan-history/host-v6.1/icon-contributions-and-resources-acceptance.md` 记录实际结果。

## 12. 测试与开发门禁

### 12.1 必测矩阵

| 层面 | 有实际意义的验收 |
| --- | --- |
| 公共包 | 原八个键不变、名称无重复、资产只读、全部路径能绘制、实际尺寸/留白/镂空正确 |
| SDK | 字段不变量、可选能力探测、返回完整键、public API 无破坏、旧插件消费者仍可加载 |
| 注册 | 同插件重复拒绝、跨插件同名允许、伪造前缀拒绝、封闭后注册拒绝、模块失败零残留、与 Document 声明顺序无关 |
| 解析 | 空/未知/非法路径默认回退、归属与可用性检查、公共键受保护、意图图标继承 Document 的 V6 规则保持 |
| UI | 新树和功能中心一致显示；浅色/深色、不同目标尺寸和缩放；同一图形多处显示无控件父级冲突 |
| 生命周期 | 浏览零插件页面初始化、缓存不保存插件对象、两个 Runtime 互不污染、关闭/重开无重复订阅 |
| 独立包 | 实际 nupkg 还原、资源包无 SDK/UI 反向依赖、ZIP 携带私有资源依赖且不携带共享 SDK DLL |
| ALC | 两插件使用不同资源包版本，Host 另用一个版本，专属注册均使用自身数据，跨边界仅为共享 SDK 描述 |
| 插件页面 | 页面直接复用资产，Standalone 不依赖真实 Host；若执行 Configure 则预览注册能力真实可用 |
| V6 回归 | 旧版默认/切换/名称记忆、树计数、多创建意图、搜索刷新、重复提交、失败重试、退出排空全部保留 |

公共包的测试项目可引用 Avalonia 做实际渲染验证，但这些依赖不得传播到资源 NuGet 包本身。
两个私有资源版本要构造可区分的真实图形并断言各自输出，不能只断言 DLL 能加载或字符串不为空。
现有插件 Registry 校验和 UI 失败日志均应有对应断言，不通过忽略异常或跳过测试制造绿色结果。

### 12.2 当前支持的开发入口

实施时再次核对当前 [Gate 配置](../../tools/MyAvaloniaManagement.Gate/gate.config.json)，以当时有效入口为准。
本次核查的正式日常命令是：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

当前 `--scope host` 与 `--scope all` 是本仓完整验证的别名；`workflow/workbench` scope 和外部路径参数已退役。
新建图标测试工程必须加入解决方案与现行 Gate 测试集合；增加私有依赖检查时同步扩展 Gate 自身的必要测试。
不恢复已删除的外部业务门禁，也不临时映射旧插件目录。

当前 verify 不采集覆盖率，需要另对实际受影响的 Host 测试来源采集并按既有 Host-only 口径合并，
与实施时的配置阈值核对。本次读取的阈值为行 84.39%、分支 70.58%；不得通过删除类型、忽略失败或降低阈值通过。
公共包和 SDK 的新增逻辑另有针对性的单元测试，不把纯资源行数混入 Host 指标。
真实插件包验收必须提供输入并确实执行，不能用缺少包时直接返回代替通过。
后续用户已授权 NuGet 包发布；执行本地开发验证、候选消费、NuGet 提交及公开源复验，不执行 Windows CI、Windows Smoke 或 Host seal。

## 13. 文档与完成标准

实施时同步更新：

- 本方案状态、`docs/README.md` 导航及 V6.1 专属实施验收记录。
- 公共资源包 README：安装、全部名称、版本、画布约定、资源来源与必要的许可归属。
- `PluginSdk.UI` README：图标契约、可选能力、最低 Host/SDK 要求与不可变注册。
- 插件开发说明与模板：专属图标、直接 builtin 引用、公共图形注册为专属键三种例子。
- V6 导航使用说明：新增引用方式，保留旧模式及默认回退的解释。
- Host 架构/兼容/测试说明：职责、ALC、候选提交、UI 缓存和最新开发门禁。

完成标准是：公共资源包可以独立消费，插件能够独立注册专属图标，Host 不再逐个维护业务图形，
三种引用方式都有真实图形和包边界证据，旧插件/V6 行为不回退，新增契约与当前开发门禁有实际结果。
未解决的基线问题、未做的桌面验证和未来发布步骤必须如实记录，不以“文档已完成”代替实现完成。

回退到 V6 时，已依赖新 SDK 的插件需要保留在支持它的 Host 上，或同步退回兼容插件版本；
仅切换为旧版目录不能撤销 SDK 依赖。单独回退某个资源包版本也要检查消费者是否使用了新成员。
显示名称、文档标题及改造序号始终不参与图标或插件的稳定身份。
