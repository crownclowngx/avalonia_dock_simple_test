# V8 交互收口与认知减负实施记录

> 日期：2026-09-11。
> 状态：实施中；G0–G4 实现和专项验证完成；G5/G6 正在汇总，不代表人工体验验收完成。
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

G1–G6 的实现、测试、视觉、覆盖率与任务观察按实际完成情况追加。没有真实参与者的任务观察必须保留“未完成”，不能以自动化测试代替。

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

中间验证暴露的旧入口反射断言、四类结果计数、异步 TextChanged 时序、分割区域活动目标及深色资源问题均已修正。没有降低门槛或跳过失败用例。最终代码之后只允许追加验收文档与证据，不据此授予发布资格。

用户已明确回复“尚未试用，保留待验收”。人工观察状态为 **待验收**，不将自动化测试写成真实用户体验结论。覆盖率仍待独立四报告汇总。
