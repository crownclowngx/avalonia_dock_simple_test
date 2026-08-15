# Managed Plugin v1 G0：绿色基线恢复记录

> 状态：已完成
> 完成日期：2026-08-15
> 分支：`dev-重构-2026年8月13日`
> 验证基线提交：`8beaab2853e3`
> 所属任务：[Managed Plugin v1 封板评审与整改任务书](../../design/host-v1-sealing-readiness-plan.md#G0恢复绿色基线)

## 1. 结果摘要

G0 已恢复可重复验证的绿色基线。锁定还原成功，整个解决方案 Release 构建为
**0 警告、0 错误**；宿主 Unit、Headless UI 和 Plugin 三套测试共 **249 项全部通过**，
Host 行覆盖率为 **76.86%**、分支覆盖率为 **63.65%**，Windows 真实窗口 Smoke 通过。

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过，未修改 `packages.lock.json` |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| `MyAvaloniaManagement.Tests` | 105/105 通过 |
| `MyAvaloniaManagement.UiTests` | 32/32 通过 |
| `MyAvaloniaManagement.PluginTests` | 112/112 通过 |
| 测试合计 | 249/249 通过，无跳过 |
| Host 覆盖率 | 行 76.86%，分支 63.65% |
| Windows Smoke | 通过，隔离目录中生成 `layout-v1.json` |

测试数量不是手写门槛。上表是 2026-08-15 的时间点证据，来自最终一次运行生成的
TRX 和 `artifacts/test-results/MyAvaloniaManagement/summary.json`；以后必须重新读取产物，
不能把 249 当成永久固定数量。

## 2. 背景、根因与影响

提交 `8beaab2` 同时引入了 Excel GET 地址生成工具、文件选择接口、生产 ViewModel 和测试。
生产接口 `IExcelFileDialogService` 包含“选择工作簿”和“选择输出 TXT”两个操作，但两个测试
Stub 只实现了前一个操作，因此解决方案最初被两个 `CS0535` 阻断：

1. `ExcelGetUrlGeneratorTests.FixedFileDialogService` 缺少
   `PickOutputTextFileAsync(string, CancellationToken)`；
2. `ExcelGetUrlGeneratorViewTests.EmptyDialogService` 缺少同一成员。

继续核对调用链后还发现两类属于同一提交的测试契约漂移：

- 插件测试仍读取已经不存在的 `OutputText`，而生产契约已经改为把完整结果写入 TXT，
  并通过 `OutputFilePath` 暴露保存位置；
- Headless UI 测试仍期待旧按钮文案和只读多行输出框，而当前生产 XAML 已经使用
  “生成全部地址到 TXT”、示例列表和输出文件路径。

这些问题均发生在测试消费者，没有证据表明生产接口或 Excel 业务行为需要在 G0 中改变。
因此 G0 只同步测试与当前生产契约，没有借机调整 Plugin SDK 或 ViewModel。

## 3. 修改边界

### 3.1 代码修改

- `ExcelGetUrlGeneratorTests.cs`
  - 用 `StubExcelFileDialogService` 完整实现文件对话框接口；
  - 分离工作簿输入路径和可选 TXT 输出路径；
  - 生成测试使用唯一临时 TXT，验证 `OutputFilePath` 和文件内容；
  - 第二次生成校验失败后重新读取文件，证明旧结果没有被覆盖；
  - 在 `finally` 中释放 ViewModel 并删除临时文件。
- `ExcelGetUrlGeneratorViewTests.cs`
  - 完整实现两个文件选择操作并以 `null` 模拟用户取消；
  - 将控件断言同步为当前按钮文案、示例列表和 TXT 输出入口。

### 3.2 明确未修改

- 没有修改 `IExcelFileDialogService` 或任何其他 public API；
- 没有拆分接口、增加 Mock 框架、测试基类或工厂；
- 没有修改 Excel 读取、URL 构造、TXT 写入、生产 XAML 或用户交互；
- 没有修改 Plugin SDK、插件加载器、Document 保存格式或数据目录；
- 没有把 G1、G2 或后续整改内容并入 G0。

## 4. 设计意图与 SOLID 约束

G0 使用最小的 **Test Double / Stub**，而不是建立新的测试基础设施。

### 4.1 输入和输出路径为什么必须分离

文件选择接口的两个方法具有不同业务角色。若一个 `path` 同时作为 `input.xlsx` 和输出路径，
生成命令会把文本写到工作簿路径，既不能真实表达用户选择，也可能覆盖输入文件。Stub 因此显式
接收 `workbookPath` 和可选 `outputTextPath`。只测试工作簿选择时不提供输出路径，返回 `null`
表示用户取消保存；需要验证生成时才传入独立临时 TXT。

这遵守单一职责原则：Stub 只提供确定的文件选择结果，文件写入及其断言仍由测试方法负责。

### 4.2 为什么 Stub 也传播取消

生产实现会在调用原生选择器前后检查 `CancellationToken`。测试替身虽然立即返回，也必须先调用
`ThrowIfCancellationRequested()`，否则替身在取消场景下不能替代生产实现。这里保护的是里氏替换
原则所要求的可观察语义，而不是模拟 Avalonia 的内部实现。

### 4.3 为什么使用 `null` 表示取消

接口返回 `Task<string?>`，生产 ViewModel 已把 `null` 或空路径解释为用户取消。Headless UI 测试
不应打开原生窗口或访问文件系统，因此两个选择操作都返回 `null` 是最小且准确的 Stub 行为。

### 4.4 为什么必须在 `finally` 清理

生成测试拥有它创建的临时文件，也负责它的完整生命周期。清理放在 `finally`，可以保证命令异常、
断言失败或测试提前结束时仍释放 ViewModel 并删除文件，避免顺序相关测试和开发机垃圾文件。

### 4.5 SOLID 取舍

- **SRP**：Stub 只替代文件选择边界，测试负责验证文件和状态；
- **OCP**：通过两个简单构造参数表达当前场景，不为一次修复建立扩展框架；
- **LSP**：保持取消和 `null` 取消选择语义；
- **ISP**：G0 完整实现现有接口，不在绿色基线任务中拆分生产契约；
- **DIP**：ViewModel 测试继续依赖 `IExcelFileDialogService`，不直接使用 Avalonia StorageProvider。

代码注释只解释上述边界、所有权和取舍，不逐行复述实现。

## 5. 验证过程与证据

### 5.1 最终验收命令

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode
dotnet build MyAvaloniaManagement.sln `
  -c Release -p:SkipPluginDeploy=true --no-restore --nologo
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
```

最终综合门禁在 2026-08-15 生成：

- `Unit/Unit.trx`：105 项通过；
- `UI/UI.trx`：32 项通过；
- `Plugin/Plugin.trx`：112 项通过；
- `coverage/Cobertura.xml` 和 `coverage/Summary.json`：合并覆盖率证据；
- `summary.json`：249 项通过、行覆盖率 76.86%、分支覆盖率 63.65%、
  `windowsSmoke: true`。

这些文件位于 `artifacts/test-results/MyAvaloniaManagement`，属于可重新生成的本地证据，
不代替仓库中的测试和门禁脚本。

### 5.2 过程中发现并关闭的漂移

第一次完整门禁在 Unit 105/105 通过后，Headless UI 因旧按钮文案失败；同步当前 XAML 文案后，
第二次运行又暴露旧“只读多行输出框”断言。将该断言改为验证当前 `ExampleUrls` 示例列表后，
定向 UI 测试为 32/32，通过后的最终完整门禁为 249/249。

保留这一过程是为了说明：G0 的完成标准是整个门禁通过，而不是仅消除最先出现的编译错误。

## 6. 风险、回滚与后续入口

G0 没有改变生产代码和公共契约，主要风险是测试与未来 UI 文案再次漂移。行为测试优先验证命令、
绑定和输出契约；只有文案本身属于产品承诺时才使用精确文本断言。

代码修复和本文档可以作为一个独立变更回滚。只回滚代码会重新产生两个 `CS0535`，并恢复
`OutputText`、旧按钮文案和旧控件形态的测试漂移；不会改变生产程序集。

G0 完成后：

- G1 可以冻结 Managed Plugin v1 支持边界和六条版本线；
- G2 可以开始把 Host 实现类型移出插件 API；
- 在 G1–G16 全部完成前，仍不得认定宿主已经封板。

## 7. 完成检查表

- [x] 两个测试 Stub 完整实现 `IExcelFileDialogService`；
- [x] 输入工作簿与输出 TXT 路径分离；
- [x] 取消语义和临时文件生命周期有代码与设计意图注释；
- [x] 失效的 `OutputText` 和 UI 控件断言已同步当前契约；
- [x] 锁定还原通过且锁文件未变化；
- [x] 解决方案 Release 构建 0 警告、0 错误；
- [x] Unit、UI、Plugin 共 249 项通过且无跳过；
- [x] 覆盖率门槛和 Windows Smoke 通过；
- [x] 测试数量来自 TRX/`summary.json`，并记录日期和命令；
- [x] 主计划、测试说明和文档索引同步更新。

后续版本与支持边界的冻结结果见
[G1：支持边界与版本线冻结记录](./g1-support-boundary-and-version-lines.md)。该链接只补充后续入口，
不改变 G0 在 2026-08-15 记录的 249 项历史基线。
