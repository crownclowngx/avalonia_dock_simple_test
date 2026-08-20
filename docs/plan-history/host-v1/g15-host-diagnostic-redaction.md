# G15：宿主诊断脱敏

> 完成日期：2026-08-20
> 状态：已完成
> 诊断 schema：`1`（字段结构未变）
> 专项入口：`scripts/Test-HostDiagnosticRedaction.ps1`

## 1. 结果与边界

G15 已把诊断安全边界固定在 `HostDiagnosticDraft` 到 `HostDiagnosticRecord` 的唯一转换处。
内存快照、插件状态 Tool、启动失败窗口、剪贴板摘要、默认 Trace/stderr 和持久化 JSON Lines
现在共享同一份白名单事实，不再依赖每个调用点自行删字符串。

本次没有修改 Plugin SDK public API、Document 七字段信封、布局格式、插件内容格式或成功/失败控制流。
`HostDiagnosticRecord` 继续使用 `schemaVersion = 1` 和原有 JSON 属性；兼容字段
`technicalDetail` 仍存在，但语义已收窄为生命周期阶段和毫秒耗时组成的受控摘要。

## 2. 数据流与信任边界

```text
插件、文件系统、Document、异常
            │  可能含正文、凭据、URL、路径和插件控制文本
            ▼
 HostDiagnosticDraft（仅错误码、阶段、强类型/结构字段和 Exception 引用）
            │
            ▼
 HostDiagnosticRedactionPolicy（校验、固定说明、异常类型投影）
            │
            ├── HostDiagnosticRecord ── 内存 / UI / 剪贴板 / JSONL
            │
            └── 默认镜像 ───────────── 仅受控记录字段
```

`HostDiagnosticDraft` 不再接受自由 `UserMessage` 或 `TechnicalDetail`。用户说明只根据宿主拥有的
错误码和阶段生成固定文本；未知但结构合法的错误码只得到阶段级固定说明。这样，清单未知属性名、
入口程序集文本、插件类型名或异常消息即使来自以后新增的调用点，也没有直接进入长期记录的参数位置。

白名单转换规则如下：

| 输入 | 记录规则 |
| --- | --- |
| 错误码 | 只接受大写 ASCII、数字、点、下划线和连字符；非法值替换为 `HOST_DIAGNOSTIC_INPUT_REJECTED` |
| 阶段、严重程度、处置 | 使用宿主枚举和固定错误分类 |
| Plugin ID | 草稿只接收已构造的 `PluginId` |
| 插件目录 | 只保留不含路径的合法叶名称；非法值丢弃 |
| 程序集 | 草稿接收 `AssemblyName`，记录只保留合法简单名 |
| 稳定 ID | 再经 `DocumentTypeId.TryParse` 校验；失败丢弃 |
| 版本与兼容区间 | 接收 `Version` / `PluginVersionRange`，由宿主格式化 |
| Exception | 只读取运行时类型；禁止读取 `Message`、`StackTrace` 或 `ToString()` 写入记录 |
| TechnicalDetail | 仅允许 `stage=<枚举>` 和 `durationMs=<Invariant 数字>`；否则为 `null` |
| UserMessage | 只由错误码/阶段固定映射生成，调用点不能提交文本 |

脱敏失败采用“丢弃可选字段或固定拒绝记录”的降级方式，不能因为日志输入异常反向终止宿主。
诊断文件写入失败也只产生内存记录；镜像、Trace 或 stderr 不可用时不会覆盖原业务结论。

## 3. 旁路入口收口

- 插件扫描、清单、加载、类型预检、模块组合、DI 保护、Registry 和 View 创建不再把绝对路径、
  插件异常正文、类型列表或服务描述符明细提交给诊断记录。
- `PluginLifecycleManager` 保留 `PluginLifecycleState.ErrorMessage` 签名，但初始化/关闭失败只返回
  `插件初始化失败。` 或 `插件关闭失败。`；默认 stderr 只包含错误码、插件 ID 和异常类型。
- `DocumentPersistenceErrorMapper` 统一打开、保存、恢复备份及宿主 Tool 入口的固定错误说明，
  不再信任公共 `DocumentLoadException.Message`，文件不存在时也不显示完整路径。
- `DocumentLifetime`、`PluginModuleCatalog`、布局存储和生命周期取消回调的直接 Trace/Console
  已按相同规则收口，避免绕过 `HostDiagnosticRecord`。

## 4. 显式本地敏感调试

只有进程环境变量精确等于下列值时，宿主和 Common 生命周期边界才会把原始异常写到临时
Trace/stderr：

```powershell
$env:MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS = '1'
```

开启后每次输出都带有显著中文风险警告。该旁路不写配置、不持久化、不接受 `true`、`yes` 等模糊值，
也不会把原文写入 `HostDiagnosticRecord`、插件状态、启动失败窗口、剪贴板或 JSONL。使用完应关闭
进程或执行：

```powershell
Remove-Item Env:MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS
```

该开关只适用于用户明确确认的本地短时排错。Release 门禁不设置它；终端或 Trace 监听器写入失败
会被安全忽略。

## 5. SOLID 与朴素设计

- **SRP**：`HostDiagnosticSession` 只管理会话、内存、文件和镜像；`HostDiagnosticRedactionPolicy`
  只负责草稿到记录的白名单转换；文档错误映射与敏感调试旁路各自独立。
- **OCP**：增加稳定错误码或受控阶段时扩展固定映射即可，不需要改 JSON schema 或所有 UI 消费者。
- **LSP**：生命周期状态、Document 操作结果和诊断记录的既有类型/控制流保持不变，仅收窄文本内容。
- **ISP**：没有向 Plugin SDK 增加日志接口、脱敏策略或调试开关；插件无需依赖宿主日志实现。
- **DIP**：加载、布局和组合代码仍只依赖 `IHostDiagnosticSink`，不依赖 JSONL、窗口或 stderr。

实现只使用一个显式白名单转换、一个固定错误说明映射和两个程序集内敏感输出帮助类。没有引入
通用日志框架、反射式脱敏器、正则替换流水线、策略工厂或 AIFLOW。白名单比“发现敏感词再替换”
更容易证明：未列出的信息根本没有进入记录的构造路径。

## 6. 测试矩阵

`HostDiagnosticsTests` 使用同一异常同时放入密码、Cookie、Bearer Token、签名 URL、Windows/Unix
绝对路径、请求/响应样例和 Document 正文 canary，并覆盖：

| 风险 | 自动化证据 |
| --- | --- |
| 内存、JSONL、默认 stderr 泄漏 | canary 在三处均不存在；异常类型和合法身份仍保留 |
| schema 或逐行格式漂移 | `schemaVersion` 仍为 1，JSONL 每行独立解析 |
| 非法结构字段 | 错误码固定拒绝，路径/非法简单名/稳定 ID 丢弃 |
| 调试开关误开 | 未设置和 `true` 均无原文；精确 `1` 仅临时输出含原文与警告 |
| 插件生命周期/UI | 初始化、关闭、取消回调、插件状态 Tool、启动失败复制摘要均验证 canary 缺失 |
| Document 错误 | 恶意 `DocumentLoadException.Message`、恢复/保存/I/O/备份失败不泄漏且不错误提交状态 |
| 源码旁路回归 | 专项 PowerShell 扫描异常正文、自由详情、路径 Console 和开关位置 |

## 7. 验收证据

2026-08-20 在 Windows x64、PowerShell 7、.NET SDK 10.0.302 上得到：

- `HostDiagnostics` 专项：**26/26** 通过；
- G15 源码门禁：检查 **127** 个生产 C# 文件，通过；
- Host Unit / Headless UI / Plugin：**173 / 38 / 150**，合计 **361/361**，无跳过；
- Host 行覆盖率 **81.12%**，分支覆盖率 **66.85%**；
- 解决方案 Release `-warnaserror` 构建：**0 警告、0 错误**。
- 临时审计快照 `a55a7f535772684793c518e1b4f54b1a08010180` 的单轮完整九阶段通过：
  SDK 包/API、四插件确定性包矩阵和 Windows Smoke 均通过。

宿主三套测试证据位于 `artifacts/test-results/MyAvaloniaManagement/`，数量和覆盖率来自本轮
TRX、Cobertura 与 `summary.json`，不是脚本中的固定数字。单轮隔离审计证据位于
`artifacts/release-gate/20260820-114346-a55a7f535772/pass-1/`。按维护者要求，独立 NuGet 包消费/API
核心门禁本次只执行一轮，不在第 2 轮重复；因此本记录不把该次验收表述为新的 G14 两轮一致性证据，
G14 原完成记录仍保留其当时的两轮历史事实。

可重放专项命令：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --filter FullyQualifiedName~HostDiagnostics
.\scripts\Test-HostDiagnosticRedaction.ps1
```

首次临时审计在任何还原或产品测试前，由 G14 核心阶段明确失败：`GetNewClosure()` 创建的阶段闭包
无法按名称解析脚本级 `Invoke-PowerShellChecked`。门禁没有继续执行或产生放行结论。修正为显式捕获
受检进程启动脚本块后，G14 核心测试和上述单轮九阶段全部通过。这一修正只影响门禁编排作用域，
不改变生产诊断或发布制品。

## 8. 回滚边界

可以整体回滚白名单转换和固定错误映射，但回滚后不得继续宣称默认诊断经过脱敏，也必须从 G14
发布入口移除相应通过结论。`schemaVersion = 1` 与字段结构不应因实现回滚而变化。敏感调试旁路可以
独立删除；删除只会失去本地原文排错能力，不应恢复任何默认原文输出。文档错误提示可以独立改写为
新的宿主固定文本，但不能重新使用异常消息或文件路径拼接。
