# V8 交互收口与认知减负实施记录

> 日期：2026-09-11。
> 状态：V8 实现、自动化开发验证、覆盖率、Headless 视觉与文档已完成；人工试用按用户明确回复保留待验收，整体交互验收尚未完成。
> 范围：本仓 Host、测试与文档；不使用 AIFLOW、Windows CI、seal 或发布 Windows Smoke，不上传或部署，`publishable=false`。

## 1. 方案与源码基线

- [V8 方案](../../design/host-v8-cognitive-ux-convergence-plan.md)已由用户确认并授权实施及阶段 Git 提交。
- 实施前 HEAD：`09ce9c2`。工作树仅有上一轮生成的 V8 方案和文档导航，无未提交生产代码修改。
- .NET SDK：`10.0.302`；产品 `3.0.0`，Core/UI SDK `3.4.0`，本轮不提升版本。

## 2. G0 本地开发基线

执行：`dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`。

[Gate summary](../../../artifacts/gate/20260911-134255-09ce9c2d235b/summary.json)：完整 verify 通过，Release build 零警告、零错误。

| 测试组 | 通过 | 失败／跳过 |
| --- | ---: | --- |
| SDK | 91 | 0／0 |
| Host Unit | 386 | 0／0 |
| Host Plugin | 211 | 0／0 |
| Host UI | 91 | 0／0 |
| MyPlugTest | 11 | 0／0 |
| 真实包验收 | 1 | 0／0 |
| 合计 | 791 | 0／0 |

verify 未采集覆盖率，最终实现后另采四份 Host 报告并校验阈值。该基线不包含新增 V8 行为、人因任务观察或发布验证。

## 3. 后续证据

G1–G6 的实际实现与验证见第 4–9 节。用户明确尚未试用，人工任务观察保留待验收，不能以自动化测试代替。

## 4. 已提交到工作树的行为与设计核对

- 欢迎“开始使用”、文件“功能中心…”汇合到原 `FunctionCenterWindowService`，保留单实例和旧插件目录。旧欢迎入口的无 UI 测试迁移为真实 Headless 窗口测试，纯回调测试继续保留。
- 四类结果用 `CommandPaletteIdentity`、`FunctionPaletteIdentity`、`PagePaletteIdentity`、`ToolPaletteIdentity` 表达；不新增 SDK Contribution，不把未授予菜单发现许可的普通 Command 暴露出来。
- `WorkbenchTextMatch` 仅计算确定的文本等级；创建目录、工具中心和 Palette 共享规则，没有引入搜索框架、历史评分或 I/O。
- `WorkspaceSession.Pages` 保存已发布页面的运行期 ID 和显示序号，关闭时退订并移除引用；UI 只取得不可变数据。现有 Adapter 没有可用于同名页面区分的独立 Dock ID，因此补充 Host internal `WorkspacePageId`，未修改现有 SDK ID 或布局格式。
- 分割页面定位暴露了原“只读取初始 DocumentDock”的限制。现在沿用唯一 `_publishedActiveDocument`，由真实激活通知更新，空活动目标仍撤回；没有新增第二份文档 Context。新增分割区域定位测试与原保存状态复查测试同时约束这个修正。
- `WorkspacePaletteActions` 适配原 Coordinator、Workspace 和 Tool Actions；普通 Command 继续原路径。View 只处理会话、忙碌、选择和提示。创建失败无半发布页面；关闭等待与退出再次复核。
- 焦点属于 Presentation：等 Dock 布局完成，再对仍有效的目标聚焦。Workspace 不遍历视觉树。搜索背景和文字使用真实 Host 主题资源，图标使用 `AppInfoBrush`，避免透明背景和深色低对比度。
- 工具更多菜单捕获打开时的行身份；列表刷新、来源／搜索变化不改变操作目标。分类、常用排序仍可达，全隐藏明确作用于整个工作区。
- 中文注释记录所有权、异步回调、失败恢复、焦点时序与不复制 Dock 模板的原因。没有增加全局服务定位器、通用策略注册中心或新的持久化层。

Dock 样式依据当前 NuGet 12.0.0.2 对应的[上游模板](https://github.com/wieslawsoltes/Dock/blob/ebbf3d46a9c9e7b4d6b898df2a685a2f956344ee/src/Dock.Avalonia.Themes.Fluent/Controls/ToolChromeControl.axaml)：仅覆盖 Tool 专属资源键和 `PART_CloseButton` 选择器，未复制或替换 Document 模板。

## 5. 新增自动化覆盖

`CognitiveUxV8Tests` 新增 12 个用例（含参数组合），`CognitiveUxV8UiTests` 新增 8 个用例（含双主题）；欢迎旧用例迁移一项，所以全套净增加 19 项。

| 方案测试项 | 当前证据 |
| --- | --- |
| T01–T04 | 欢迎／菜单同窗口，目录无需 Command，多意图与来源同名不合并，真实目录排序和纯匹配边界 |
| T05–T11 | 稳定选择、清除来源、查询无初始化、失败释放与重试、重复 Enter、忙碌 Esc／主窗口关闭保护 |
| T12–T15 | 同名页面身份和序号、内容保留、过期目标拒绝、Host 标题与修改标记、分割区焦点、原 Command 状态／租约／快捷键全套 |
| T16–T18 | 更多菜单原行目标、键盘打开／Esc、分类及常用排序迁移测试、搜索约束下全工作区隐藏；部分失败沿用 ToolCenterTests |
| T19–T21 | 原四向工具恢复与引用复用套件、真实 Channel 工作线程隐藏期间从 1 项推进到 3 项、Tool × 提示及可访问名称 |
| T22–T25 | 新建／切换成功焦点、失败重试；原窗口关闭与多 Runtime 测试；SDK／旧 SDK／真实包；100 个功能入口与 100 个工具、双主题长名称和空态 |
| T26 | HelpContent、快速开始、工具中心、V8 操作说明同步；最终链接检查见后续记录 |

上述覆盖不表示每个排列组合均独立新增了测试。SDK、关闭保存、四向恢复、可用性、双 Runtime 和资源所有权继续依靠既有回归套件；实际 Windows 多屏和 DPI、外部业务插件任务仍不是自动化夹具的证据范围。

## 6. 实现阶段完整开发验证

最终生产代码的 [Gate summary](../../../artifacts/gate/20260911-142715-27b374cca2d4/summary.json) 为通过：SDK 91、Host Unit 397、Host Plugin 211、Headless UI 99、MyPlugTest 11、真实包 1，合计 **810**，零失败、零跳过；Release 构建零警告、零错误，契约检查通过。

中间验证暴露的旧入口反射断言、四类结果计数、异步 TextChanged 时序、分割区域活动目标及深色资源问题均已修正。没有降低门槛或跳过失败用例。最终代码验证之后仅追加验收文档与证据，不据此授予发布资格。

用户已明确回复“尚未试用，保留待验收”。人工观察状态为 **待验收**，不将自动化测试写成真实用户体验结论。四份独立覆盖率已汇总通过，见下一节。

## 7. 独立覆盖率与可重现口径

生产代码提交：`a1c7eb5`。后续提交仅追加架构说明、验收文档和证据，不改变已经验证的生产代码或测试。此次覆盖率沿用同一 Release 构建与既有 `coverage.runsettings`，未增加排除项。

| 独立报告 | 通过数 | 失败／跳过 |
| --- | ---: | --- |
| Host Unit | 397 | 0／0 |
| Host Plugin（排除 PackageAcceptance） | 211 | 0／0 |
| Host Headless UI | 99 | 0／0 |
| MyPlugTest PackageAcceptance | 1 | 0／0 |
| 合计 | 708 | 0／0 |

只合并这四次测试的顶层 GUID 目录中的原始 Cobertura。VSTest 另有 TRX 附件副本，不重复合并；合并后的报告也不作为输入。包验收使用最终 verify 解出的 `Controls`，没有拿源码直引替代真实包。

| 指标 | 实测 | 现有最低阈值 | 结论 |
| --- | ---: | ---: | --- |
| Host 行覆盖率 | **88.18%** | 84.39% | 通过 |
| Host 分支覆盖率 | **72.78%** | 70.58% | 通过 |

[覆盖率汇总 JSON](../../../artifacts/v8-cognitive-ux/coverage-final/summary.json)保存四条完整命令及原始报告位置；[HTML 报告](../../../artifacts/v8-cognitive-ux/coverage-final/merged/index.html)可查看逐文件结果。[提交内证据摘要](./development-evidence.json)保存源码提交、测试计数、阈值与原始报告 SHA-256；大体积 TRX／覆盖率报告保留在本地 artifacts。

复查使用当前 SDK 和现有工具，不运行 seal：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
# Unit、Plugin、UI 分别执行下列命令，替换项目与输出目录；禁止复用同一输出目录。
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj -c Release --no-build --no-restore -m:1 --filter 'Category!=PackageAcceptance' --settings Host/MyAvaloniaManagement.Tests/coverage.runsettings --collect:'XPlat Code Coverage' --logger 'trx;LogFileName=unit.trx' --results-directory artifacts/v8-review/unit
# 第四次使用 PluginTests 项目与 Category=PackageAcceptance，并把
# MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT 指向本次 verify 解出的 Controls。
# ReportGenerator 只接受四个 GUID 原始报告，以分号连接：
dotnet reportgenerator '-reports:<unit>;<plugin>;<ui>;<package>' '-targetdir:artifacts/v8-review/merged' '-reporttypes:Cobertura;Html' '-assemblyfilters:+MyAvaloniaManagement'
```

按当前 `gate.config.json` 阈值检查合并报告，只允许一个非空 `MyAvaloniaManagement` 程序集。`verify` 的测试 ZIP 仅作本地验收输入，`releaseEligible=false`、`publishable=false`。

## 8. 视觉、文档与 SOLID 审查

通过实际 XAML 与 Headless Skia 渲染复查了功能中心、工具中心、命令面板的浅色／深色、长名称、滚动、空态及错误态。搜索面板为 800×650 场景，工具中心另含 720×600 紧凑场景；无 Windows 桌面或 DPI 结论。

- [深色长名称搜索](./images/palette-long-dark.png)
- [失败后保留查询和选择](./images/palette-failure.png)
- [工具中心简化后的主操作](./images/tool-center-light.png)

其余可重生成图像位于 `artifacts/v8-cognitive-ux/screenshots`；使用 `MYAVALONIA_V8_RENDER_DIRECTORY` 配合 `CognitiveUxV8UiTests`，旧功能／工具窗口图像仍使用 V6／V7 的渲染环境变量。

| 原则 | 本轮具体落实 |
| --- | --- |
| S 单一职责 | 纯匹配、只读候选、工作区所有权、用例适配和窗口会话分别承担明确工作 |
| O 开闭原则 | 新结果在 Host 展示层扩展，不修改 SDK Command／Document 契约或插件初始化协议 |
| L 里氏替换 | 设计器与生产继续履行现有展示接口；普通命令与三类工作区动作保持各自正确的执行契约 |
| I 接口隔离 | 欢迎页只接收两个无参动作；UI 使用只读投影和窄用例，不新增“万能工作区接口” |
| D 依赖倒置 | 组合根完成注入；业务边界沿用现有存储、工厂和交互端口，不把 Provider 下放给 ViewModel |

新建少量具体类型和纯函数，没有为了单实现创建接口族。现有生命周期、关闭保存、程序集隔离和真实包约束均通过完整测试与架构契约检查。中文注释说明设计原因和关键时序。

路径与代码围栏扫描共检查 327 条本地链接，新增失效路径为 0，已有失效路径为 27（其中包含重复引用）。[扫描记录](../../../artifacts/v8-cognitive-ux/document-links.json)保存完整明细。本次扫描同时发现根 README／文档导航中的既有跨仓链接指向本机缺失的旧相邻目录；这些链接在基线中已经存在，本轮没有改写外部仓库或把它们伪报为通过。V8 新增本仓使用说明、架构、验收与图片链接均可解析。

## 9. 人工观察与交付边界

用户答复：**“尚未试用，保留待验收”**。据此 G5 的人工任务观察继续待验收；G6 已同步这一状态，不能将 V8 整体交互验收标成完成。

后续人工记录至少包括：从欢迎页找到并打开功能、搜索并切换同名已有页面、隐藏并恢复工具、模拟失败后重试；记录完成情况、误操作和需要提示的位置。本次未做真实用户操作计时、Windows 多显示器／100%–200% DPI、原生窗口焦点体验或外部插件实际业务联调。

代码与本地自动化验证已交付。没有使用 AIFLOW、Windows CI、seal、发布 Windows Smoke，没有上传、发布 tag、改动用户数据或部署；产品 `3.0.0`、Core/UI SDK `3.4.0` 和现有 schema 保持不变。回退实现提交不需要 V8 数据迁移。
