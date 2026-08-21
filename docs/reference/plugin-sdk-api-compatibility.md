# Plugin SDK API 兼容基线维护指南

> `managed-plugin-v1.0.0` 继续定位 SDK `1.0.0` 的历史正式源码基线。V2 G1 已把活动版本切到
> `2.0.0`/`ApiCompatibility/v2`，但 G2 尚未重建最终 SDK；因此当前 V2 表面全部属于 Unshipped，
> 不表示 V2 NuGet 或 public API 已发布、冻结或可供外部消费。

## 1. 目的与权威源

本文是维护 `MyAvaloniaManagement.PluginSdk` public API 的长期知识入口，供开发者、评审者和 AI
共同使用。签名事实的权威源位于：

```text
Host/MyAvaloniaManagementCommon/ApiCompatibility/<vN>/
├── PublicAPI.Shipped.txt
└── PublicAPI.Unshipped.txt
```

基础 NuGet 包名仍是 `MyAvaloniaManagement.PluginSdk`。G2 完成前，包内契约程序集仍名为
`MyAvaloniaManagementCommon`，UI 包仍是 dependency-only Profile；这些是未发布阶段桥，不是最终
Core/UI 形状。只有当前契约程序集进入 API 基线，Host 可执行程序集始终是实现细节。

活动目录由根级 `Directory.Version.props` 的 `MyAvaloniaPluginSdkApiBaseline` 选择。该值必须与
`MyAvaloniaPluginSdkVersion`、`MyAvaloniaPluginSdkAssemblyVersion` 的主版本一致。

## 2. 两类 API 文件

### 2.1 Shipped

`PublicAPI.Shipped.txt` 保存已经发布或已经冻结为当前 vN 承诺的完整签名。G13 建立的 v1 文件是
G11 完成破坏式收口后的第一份正式基线。条目包括类型、构造函数、方法、参数名与类型、返回类型、
属性访问器、事件、字段、泛型约束和 nullable 标注。

Shipped 不是“重新生成后覆盖”的快照。删除或改写其中的条目表示撤回已经建立的承诺，必须按主版本
升级流程处理。

### 2.2 Unshipped

`PublicAPI.Unshipped.txt` 保存同一主版本内已经完成 API 评审、但尚未归档到下一次正式发布的兼容新增。
代码新增 public 成员后，构建首先产生 `RS0016`。开发者确认该成员确实应成为 SDK 契约后，才把诊断
显示的完整签名加入 Unshipped。

正式发布一个 SDK 次版本时，应把该版本实际发布的 Unshipped 条目按 Ordinal 顺序移入 Shipped，
并让 Unshipped 恢复为只包含 `#nullable enable`。无论条目暂存在 Shipped 还是 Unshipped，后续删除
都会触发兼容错误。

### 2.3 G1 的 V2 过渡规则

G1 的 `v2/PublicAPI.Shipped.txt` 只有 nullable 头；`Unshipped` 与未修改的 v1 Shipped 表面一致。
这是为了在版本线先切到 2 的同时保持普通构建可验证，并明确允许 G2 在发布前重建整个契约。
不得把这些条目移入 V2 Shipped、发布 V2 包或据此承诺兼容；G2 必须在同一变更中完成新 API、依赖
边界、消费者夹具和最终 V2 文本基线。

## 3. 日常变更流程

### 3.1 内部实现变更

只修改 internal/private 实现时，不应修改 API 文本。运行：

```powershell
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2
```

脚本应直接通过。若出现 public 差异，先判断是否误扩大了可见性或修改了签名，不要先编辑基线。

### 3.2 兼容新增

1. 先用最小 public 类型或成员表达真实插件用例，并补齐中文 XML 文档。
2. 构建确认 `RS0016` 只列出预期新增，没有旧成员的 `RS0017`。
3. 审查所有权、命名、错误语义、依赖方向和外部类型泄漏。
4. 将完整签名登记到活动目录的 `PublicAPI.Unshipped.txt`，保持 Ordinal 排序。
5. 运行 G13 脚本、SDK 包消费门禁、宿主测试和真实插件构建。
6. 同步兼容契约、迁移说明或示例；没有使用方式的新增不应仅靠登记签名进入 SDK。

显式登记不是破坏性变化的豁免。一次变更同时出现 `RS0016` 和 `RS0017` 时，通常表示重命名、改参数
或改返回类型，应先按破坏性变化处理。

### 3.3 典型破坏性诊断

| 变化 | 常见诊断表现 | 处理 |
| --- | --- | --- |
| 删除类型或成员 | `RS0017` 指向原完整签名 | 拒绝，保留原契约或进入新主版本流程 |
| public 改为 internal/private | 原类型或成员产生 `RS0017` | 拒绝 |
| 修改参数名、类型、顺序或数量 | 旧签名 `RS0017`，新签名 `RS0016` | 拒绝 |
| 修改返回类型 | 旧返回签名 `RS0017`，新返回签名 `RS0016` | 拒绝 |
| 改变 nullable 契约 | nullable 相关 RS 诊断或签名增删 | 先评估调用方；不能仅重写文本 |
| 重复登记 | `RS0025` | 删除重复项并保持单一事实 |
| 基线文件内容无效 | `RS0024` | 修正签名文本，不得忽略诊断 |
| Shipped 或 Unshipped 缺失 | `RS0048` | 恢复活动主版本的两份基线文件 |

`PublicApiContractTests` 只保留事件总线等行为语义断言，不再生成第二套反射指纹。Host 零导出面继续由
`HostApiBoundaryTests` 保护。

## 4. 禁止的绕过方式

- 不得删除 Shipped 条目后声称“基线已更新”。
- 不得在同一主版本的 Unshipped 中使用 `*REMOVED*` 接受删除；专项脚本和政策测试会阻断。
- 不得把 RS 诊断加入 `NoWarn`、降低为提示或移除 `require_api_files`。
- 不得把 Host 实现或 UI Profile 加入基础 SDK 基线来掩盖依赖方向问题。
- 不得只提高哈希、文件名或版本数字而不提供迁移和真实插件消费证据。

这些限制使 API 变更必须显式进入评审，而不是让工具替设计者决定兼容性。

## 5. 后续新主版本流程

确需破坏性变化时，按一个完整变更单元执行：

1. 说明真实用例、替代设计、受影响插件和不能保持兼容的原因。
2. 在新的 `ApiCompatibility/vN` 建立 Shipped/Unshipped，不修改任何历史主版本目录。
3. 同步提升 SDK PackageVersion、FileVersion、AssemblyVersion，并把活动基线切到新目录。
4. 更新 Managed Plugin 的 SDK 兼容区间，不得继续声明未验证的旧区间。
5. 更新迁移说明、最小 SDK 包消费者和所有仓库插件源码。
6. 执行锁定还原、Release 零警告构建、G13、SDK 包消费、宿主三层测试、Windows Smoke 和四插件包矩阵。
7. 由所有者审阅并记录回退边界；旧基线继续留在仓库供历史插件和差异复核。

只创建新基线目录但不完成版本、清单兼容区间和消费者迁移，不构成合法主版本升级。

## 6. 标准门禁与排错

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode -p:SkipPluginDeploy=true --nologo
dotnet build MyAvaloniaManagement.sln -c Release -p:SkipPluginDeploy=true --no-restore --nologo -warnaserror
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
```

以上是当前非发布检查，不包含 Windows Smoke、发布总门禁或上传。实际发布时再按对应发布计划增加
平台和制品验收，不能用 G1 结果冒充发布放行。

排错顺序：

1. 先阅读首个 RS 诊断中的完整成员，不要先改文本。
2. 若只有 `RS0016`，确认是有意新增还是意外 public。
3. 若有 `RS0017`，查找同名 `RS0016`；同时存在通常表示签名被修改。
4. 若提示缺少 API 文件，检查活动基线、目录名和 `.editorconfig`，不要关闭分析器。
5. 若真实 SDK 通过但测试副本负例未失败，检查脚本唯一替换哨兵和预期成员，不要放宽诊断断言。

专项脚本只删除自身创建的系统 Temp GUID 子目录，不读写用户数据根，不修改仓库源文件，也不发布 NuGet。

## 7. 评审清单

- [ ] 变化只涉及正式基础 SDK，或已明确说明为什么不影响 public API。
- [ ] 新增签名在 Unshipped 中显式可见，且存在真实消费者或明确使用方式。
- [ ] Shipped、Unshipped 稳定排序、无重复、无 `*REMOVED*`。
- [ ] Analyzer 保持私有构建依赖，没有进入 nuspec 或插件还原图。
- [ ] Host 实现仍无自有 public 导出；G2 前 UI Profile 仍明确标记为未发布阶段桥。
- [ ] 破坏性变化已提升主版本并同步清单区间、迁移、样例和真实插件。
- [ ] G13、包消费、宿主与插件门禁均有本次执行证据。
