# V13 插件启用与禁用开发验证

> 当前状态（2026-09-21）：Host 整体人工验收由项目所有者手工使用后确认通过，见[人工验收确认](../archive/records/host/manual-acceptance-20260921.md)。本文保留专项命令和后续回归矩阵；原阶段自动化结果以关联记录/JSON 为准，当前交付见[本机部署](local-deployment.md)。

> 用途：为 [V13 实施计划](../archive/plans/host-v13-plugin-enablement-plan.md)提供专项测试矩阵、本地开发门禁、恢复约定与证据标准。
> 状态：专项已执行，实际名称、结果与最终完整 verify 见[开发记录](../archive/records/host-v13/development-acceptance.md)及其 JSON；原开发阶段未执行原生桌面，后续所有者确认见页首。核对日期：2026-09-21。
> 事实源：[Gate 顺序计划](../../tools/MyAvaloniaManagement.Gate/GateExecutionPlan.cs)、[Gate 配置](../../tools/MyAvaloniaManagement.Gate/gate.config.json)、[主仓验证](verification.md)及当前 Host 测试工程。

## 1. 验证范围与约束

只使用本地开发测试和 `verify`。不使用 AIFLOW、Windows CI、`seal`、发布 Windows Smoke、发布覆盖率或发布重复性门禁，不调整发布规则、阈值或 API 基线。开发 verify 中的 MyPlugTest 打包与 ZIP 加载属于兼容验证，不是发布。

SOLID、朴素模式和详细中文注释作为必需审查项；测试证明行为，不能用测试数量代替设计审查。所有文件测试使用隔离临时数据根，禁止改写用户实际设置、Controls 或安装目录。

新增单元、Plugin 和 Headless 用例优先放入已有测试工程，由现有 verify 自动发现。下文矩阵由新测试和既有协议回归共同覆盖，实际类名及“矩阵编号 → 实际测试名称 → TRX”映射在开发记录与 JSON 中维护。

## 2. 设置与提交单元测试

已新增 `PluginEnablementSettingsTests` 和 `PluginEnablementServiceTests`，位于 Host Unit 工程。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| S01 | 首次无主文件/备份；数据根覆盖 | 默认全启用；使用 HostDataRootPolicy；不额外追加 v2，不访问真实用户配置 |
| S02 | ID 及格式往返 | 精确 PluginId 规则、稳定排序、集合去重；非法 ID、重复 JSON 字段、过大输入与不支持 schema 被拒绝 |
| S03 | 安装目录改名、升级、暂时缺失插件 | 同一 ID 沿用禁用；保存其他项时不丢失未安装 ID；重新启用只移除目标 ID |
| S04 | 原子保存成功、失败及清理失败 | 成功才更新意图；未提交保留旧文件/快照；提交后清理失败不回滚 UI 为旧值；临时文件正常清理有测试，清理失败分支与提交后无 I/O 经代码审查 |
| S05 | 主文件损坏及有效备份；仅备份存在 | 原件保留，采用有效备份时明确恢复状态并限制覆盖；不误判首次使用 |
| S06 | 无有效备份、权限错误、未来 schema | 无可靠配置时阻止本次插件加载；不能默认为全启用；未来格式不使用旧备份覆盖 |
| S07 | 连续修改、重复设置、同进程并发 | 串行提交、幂等结果正确；过期回调不覆盖较新状态；改回本次启动值取消 pending |
| S08 | 两个实例/外部编辑竞争 | 短时锁和修订核对生效；旧快照不能覆盖另一实例变更；重新读取不改变本次启动事实 |
| S09 | 存储与 UI 边界 | Store 不调用加载器；服务只改已保存意图；查询和读取不创建插件对象 |

故障注入只用于真实 I/O 提交边界；避免构建通用虚拟文件系统。权限失败优先用可控失败点，不依赖执行账户恰好没有某目录权限；并发用受控同步，不用固定 sleep。

## 3. 插件发现与加载集成测试

已新增 `PluginEnablementLoadingTests`，位于 Host Plugin 工程，复用现有真实 DLL/manifest/deps 测试资产。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| L01 | 禁用一个有效插件，另一个保持启用 | 禁用项无 ALC 和入口程序集，无模块构造、Configure、Provider、初始化或关闭回调；其他插件正常 |
| L02 | 默认设置及空禁用列表 | 原完整加载行为、精确入口、依赖隔离、失败隔离不变 |
| L03 | 禁用项 DLL/deps 缺失或 SDK 不兼容 | 清单身份可展示，启动过滤发生在加载/兼容评估前；不执行插件代码、不谎报已兼容 |
| L04 | 重复 PluginId，包括被禁用的身份 | 保持原致命歧义；任何插件入口代码执行前终止原启动路径 |
| L05 | manifest 损坏或缺失身份 | 原诊断保留，不能按目录名设置开关；不制造可靠 PluginId |
| L06 | 已知 ID 的加载/注册/初始化失败 | 看板仍可设置下次禁用；失败不会自动改变用户意图 |
| L07 | 同一根＋数据根重复发现；不同数据根 | 首份启动快照稳定；设置变化不触发重复加载；不同数据根的策略及候选事实不串扰 |
| L08 | 配置无法读取、全部禁用、目录为空 | Host 管理服务仍可建立；无插件运行副作用；读取错误与用户禁用分开展示 |
| L09 | 正常退出和关闭取消 | 禁用设置不改变本次已加载插件清理顺序；取消退出不提前释放；未加载项无 Shutdown 回调 |

不能仅断言 `snapshot.Assemblies.Count == 0`。需结合独立进程内已加载程序集/ALC 枚举，以及测试插件模块、Configure、生命周期的可观察探针，证明加载与执行均未发生。测试宿主不要静态引用待测插件类型导致其提前加载。

## 4. 查询、看板及窗口寿命

复用 `PluginStatusTests`、`PluginDashboardTests` 及 `PluginStatusWindowTests` 的既有断言，并新增 `PluginEnablementQueryTests`、`PluginEnablementUiTests`。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| Q01 | 已加载、已禁用、加载失败及无身份候选混合 | 不漏项、不重复；已发现包含禁用项；正常禁用不计入异常 |
| Q02 | 启用/禁用四种启动与下次组合 | pending 由策略差异产生；可用性失败不等于禁用；配置未知不套用正常四态 |
| Q03 | 全部/可用/异常/已禁用/待重启筛选 | 统计与筛选含义一致；已禁用且待启用可同时命中两个对应筛选；搜索包含新状态 |
| Q04 | 读取及复制/导出 | 无写盘、发现、加载或对象创建；当前状态和下次设置明确，摘要继续脱敏 |
| U01 | 真实控件点击开关并保存 | 成功后开关及提示更新；当前页面、贡献与插件实例不变 |
| U02 | 保存失败、只读、无可靠身份、退出中 | 明确反馈；未提交恢复原选择；不可操作状态有原因，未冒充保存成功 |
| U03 | 快速操作、保存中刷新/关窗 | 不能并发覆盖；持久化提交与 UI 取消边界正确；迟到回调不访问已释放窗口 |
| U04 | 刷新、重新激活、关闭重开、修改后改回 | 已保存意图和选中项保持；正确清除待重启；不走加载入口 |
| U05 | 键盘、窄窗口、深浅主题 | 控件可达，状态文字可读、无截断关键提示；保存与错误信息可区分 |
| U06 | 全部禁用或配置失败的 Host | 主窗口和插件看板仍可打开，用户禁用可再启用；配置恢复错误有独立说明 |

Headless 按现有 Avalonia 串行配置执行；推进 Dispatcher 验证先后与释放，不用延时掩盖竞态。测试真实绑定及命令入口，不能只调用私有方法后比较文案。

## 5. 关联回归与新进程验收

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| X01 | 三次独立进程启动往返 | A 启用并保存禁用；B 读同一配置且不加载，保存启用；C 恢复加载。每次有独立 PID、退出码及实际探针证据 |
| X02 | 禁用插件所有贡献 | 重启后 Document/Tool/Command/Placement/Icon/Workflow Action 不注册；Host 管理入口存在 |
| X03 | 工具布局和偏好往返 | 禁用后保存布局仍保留缺失项位置、显示意图和收藏；再启用恢复；Document 不被新增为跨启动恢复对象 |
| X04 | 工作流引用被禁用提供方 | 正常返回既有缺失/不可用结果，不执行、不删定义、不自动启用 |
| X05 | 未加载插件检查产物、报告导入与历史矩阵 | 只读取磁盘/PE，不加载程序集；文案区分未加载；历史报告不覆盖用户禁用或当前错误 |
| X06 | 运行中保存开关及重启退出确认 | 保存不关闭文档或后台任务；正常退出继续原取消与保存确认协议 |
| X07 | 原架构与 API 边界 | Host internal、SDK public API、manifest/schema、私有 Provider、文档 Scope 及诊断脱敏契约不回退 |

X01 由现有 Plugin 测试调用最小测试子进程夹具，使用临时 Controls 和同一隔离数据根，设置超时、取消及失败清理；子进程执行真实发现/组合路径。夹具归测试资产，不给生产 Host 新增重置缓存或执行任意测试脚本的后门，也不接入发布 Windows Smoke。仅在确需编译入口时增加最小测试项目，由已有测试工程构建依赖纳入 verify。

同进程重建 ViewModel、手动清缓存或只写读 JSON 都不能代替 X01。旧插件二进制兼容由既有验证及真实未重编译样本补证；缺样本明确写未验证，不让外部仓库成为默认开发门禁的隐藏前提。

## 6. 本地门禁与命令

命令从仓库根目录串行运行。以下命令已用于本轮专项及完整开发验证；最终实际执行记录以 JSON 为准。

### 6.1 基线及最终完整门禁

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

执行固定 Avalonia/Dock 补丁准备、locked restore、Release 零警告构建、SDK/Host Unit/Plugin/Headless UI/MyPlugTest Unit、契约及已发布 API 比较、MyPlugTest 打包与真实 ZIP 验收。verify 的执行图不包含发布覆盖率和 Windows Smoke。

基线用于区分原有失败；最终必须成功才能标记开发门禁通过。专项不能替代完整 verify，不更改 scope、过滤规则、API 基线或跳过项以获取绿灯。

### 6.2 设置、查询与关联单元专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~PluginEnablement|FullyQualifiedName~PluginStatusTests|FullyQualifiedName~PluginDashboardTests|FullyQualifiedName~PluginCompatibilityEvidenceTests|FullyQualifiedName~DockLayoutTreeTests|FullyQualifiedName~DockLayoutWorkspaceStateTests|FullyQualifiedName~ToolCenter|FullyQualifiedName~WorkflowActionKernelTests' --logger 'trx;LogFileName=v13-unit.trx' --results-directory artifacts/host-v13/unit
```

### 6.3 发现与重启专项

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~PluginEnablement|FullyQualifiedName~ManagedOnlyPluginLoadingTests|FullyQualifiedName~PluginManifestCompatibilityTests|FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~WorkflowAction)&Category!=PackageAcceptance' --logger 'trx;LogFileName=v13-plugin.trx' --results-directory artifacts/host-v13/plugin
```

PackageAcceptance 由完整 verify 准备真实包输入并验证，常规专项排除它，不能把未提供包输入造成的跳过作为通过。

### 6.4 看板与布局 Headless 专项

```powershell
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~PluginEnablement|FullyQualifiedName~PluginStatusWindowTests|FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~PluginNavigationUiTests' --logger 'trx;LogFileName=v13-ui.trx' --results-directory artifacts/host-v13/ui
```

`--no-restore` 前提是本轮已完成 locked restore。各轮保留证据时使用独立输出子目录，避免覆盖旧 TRX。组合过滤器可能只跑到旧测试，因此必须核对每个新增类和对应矩阵用例实际存在且执行；退出码 0 本身不足以证明新增功能通过。

### 6.5 工具自测及文档检查

不要求为 V13 修改 Gate 生产代码。若确需调整测试资产构建、发现或证据校验，并实际影响 Gate，则同步工具测试并串行运行：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror
```

额外检查所有本轮 Markdown 的相对文件链接、锚点、测试名、命令和状态，运行 `git diff --check`。现有 Gate 的三个 README 链接检查不能覆盖全部新文档；未跟踪的新文件也要检查。中文注释和 SOLID 边界采用人工代码审查，不编写仅验证注释字符串存在的测试。

## 7. 配置异常与恢复验收

以下行为已接入，配置格式与维护步骤以[插件开关契约](../reference/plugin-enablement.md)为准。

| 异常 | 运行与维护结果 |
| --- | --- |
| 主文件损坏，但有有效备份 | 明确显示本次采用备份；保留坏文件、禁止直接覆盖；修复后重启确认 |
| 主文件和备份均无效，或无法读取 | 保留文件，本次不加载插件；Host 和看板可用，开关写入禁用，明确告知配置错误 |
| 未知未来 schema | 保留原文件；不以旧版本规则重写，不退回可能过时的备份策略 |
| 外部实例改写了设置 | 当前保存失败并提示重新读取；本次启动策略仍为原快照；再次操作须针对最新文件 |
| 磁盘写入失败 | 旧意图与旧文件保留；本次会话插件不受影响；用户能辨别尚未保存 |

维护恢复在退出使用该数据根的所有 Host 后进行：先保留原主文件及备份，再按已确认的用户选择修复 schema 1 配置；重新启动并核对清单。删除主文件及备份会恢复默认全启用，因此不能作为无说明的自动恢复手段。本轮不制作一键重置或通用配置编辑器。

## 8. 证据、桌面检查与完成判定

必需记录日期、HEAD、已有及新增未提交差异身份、实际命令、退出码、用例通过/失败/跳过数、TRX 路径及摘要、Gate run-id、新进程探针结果。逐项建立 S/L/Q/U/X 编号与实际测试名映射；必需项零测试、缺报告、跳过或失败均不能计为通过。

开发记录写入 `docs/archive/records/host-v13/development-acceptance.md`；最终结果写入同目录的 `final-development-evidence.json`。Markdown 会嵌入 Host，应先定稿再运行最终完整 verify；实际计数、产物哈希与最终结果放在非嵌入 JSON，避免改文档后继续引用旧 DLL 身份。

本地桌面按以下矩阵分别记录“已观察/失败/未执行”和原因，不使用 Windows CI 或发布 Smoke 替代：

| 编号 | 交互检查 |
| --- | --- |
| M01 | 在真实插件看板禁用插件，当前页面与后台任务继续；退出重启后插件入口消失但看板中仍可找到 |
| M02 | 重新启用并再次重启，入口、工具布局及收藏恢复；插件业务数据仍在 |
| M03 | 保存后改回原值、关闭重开看板、搜索及筛选，待重启提示符合选择 |
| M04 | 有未保存文档时退出并取消，工作区不受开关操作影响；真正退出才执行原关闭流程 |
| M05 | 隔离数据根模拟配置错误/保存失败，验证错误反馈、Host 管理入口和修复后重启 |

Headless 或子进程测试不代表原生焦点、键盘体验及外部真实业务已经验收。缺少桌面环境时保留待验收项，不能把它改成通过；自动化开发完成、桌面完成、部署及公开发布分别记录。

最终开发通过要求：必需自动化实际执行且通过、完整 verify 成功且零警告、原有效断言及兼容政策未削弱、SOLID 与中文注释审查完成、文档与实现一致。发布资格在将来实际发布阶段另行判断。
