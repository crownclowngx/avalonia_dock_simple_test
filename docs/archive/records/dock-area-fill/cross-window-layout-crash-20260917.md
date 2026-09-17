# Document 拖入已有浮窗的布局崩溃诊断

日期：2026-09-17。状态：已取得安装版异常及独立最小复现；本轮仅诊断，未修改生产代码、重新编译或部署修复版。

## 结论与证据

直接崩溃原因是跨窗口复用控件后，旧窗口的布局管理器继续处理已经归属新窗口的控件。Avalonia 12.1.2 在安排布局时发现控件的布局根与当前 LayoutManager 不一致，抛出未处理的 `ArgumentException`，最终终止进程。

Windows Application 日志中三次 `.NET Runtime` 事件（ID 1026）完全一致：

| 本地时间 | RecordId | 异常 |
| --- | --- | --- |
| 09:11:28 | 210160 | Attempt to call InvalidateArrange on wrong LayoutManager. |
| 09:12:14 | 210163 | 同上 |
| 09:12:44 | 210166 | 同上 |

调用栈从 `LayoutManager.InvalidateArrange`、`ExecuteArrangePass`、`InnerLayoutPass`、`ExecuteLayoutPass` 进入渲染调度，最终逃逸出 `Program.Main` 的桌面主循环。该异常栈没有记录具体控件实例，不能据此断言是哪一个百度网盘子控件出错。

安装文件 `D:\data\avalonia\MyAvaloniaManagement.exe` 的 SHA-256 为 `0CF8511BBAA3559EF63F9C17DB09F662C10261BEC6988174236FD6C42B81F59F`，与[本次部署记录](local-deployment-20260917.json)一致。实际配置为 Avalonia 12.1.2、Dock 呈现补丁 12.1.0.7-area.5。

## 触发机制

1. 旧窗口的布局队列中存在待安排的正文控件或其后代。
2. 拖放提交后，宿主为保留 Document、View 和作用域，复用同一个正文实例，将其从旧窗口转交给已有浮窗。
3. 新窗口尚未完成测量时，旧窗口先执行遗留布局任务。
4. Avalonia 把需要重新测量的控件放入 `_toArrangeAfterMeasure`，随后仍调用旧管理器的 `InvalidateArrange`；其所属布局根已经变为新窗口，于是触发一致性检查异常。

依据：[Avalonia 12.1.2 LayoutManager 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Base/Layout/LayoutManager.cs)，重点为 `ExecuteArrangePass` 的重新入队与 `InvalidateArrange` 的布局根检查。

宿主相关交接入口是 [DocumentControlRecycling.Build / RemoveFromVisualParent](../../../../Host/MyAvaloniaManagement/Business/Docking/DocumentControlRecycling.cs)：现有逻辑通过 `ContentPresenter.UpdateChild` 立即摘除旧父级，保证 View 的唯一宿主，但这不等于旧窗口布局队列中的引用已经处理完毕。结合安装版堆栈与独立复现，应首先调查这一跨 TopLevel 交接边界；具体生产控件与完整鼠标时序仍需宿主定向回归确认。

## 独立最小复现

诊断工程位于 `artifacts/dock-area-fill/layout-crash-repro`，只引用 Avalonia Headless 12.1.2 和测试工具，没有 Dock、宿主或百度网盘插件。使用两个普通 Window，将包含 TextBlock 的 Border 从第一个窗口转移到第二个窗口，再分别执行布局。

```powershell
dotnet test artifacts/dock-area-fill/layout-crash-repro/LayoutCrashRepro.csproj -c Release -m:1
```

| 对照时序 | 实际结果 |
| --- | --- |
| 直接跨窗挂接，然后先刷新旧窗口 | 失败，复现同一异常和布局调用栈 |
| 转移之前先刷新旧窗口，然后直接挂接 | 失败；摘除本身仍可产生待处理任务 |
| 完整摘除，保持脱离状态刷新旧窗口，然后挂入新窗口 | 通过 |
| 挂接后先完成新窗口布局，再刷新旧窗口 | 通过 |

最终报告为 **4 项：2 个预期复现失败、2 个对照通过、0 跳过**，不是修复后全绿报告。证据保存在 `results/layout-sequencing.trx`、`sequencing.log` 和 `windows-runtime-events.json`。该对照证明布局时序可以触发问题，不依赖百度网盘业务；通过的对照不等于已得到可直接上线的通用修复。

## 原有测试为何没有发现

[DockAreaFillUiTests](../../../../Host/MyAvaloniaManagement.UiTests/DockAreaFillUiTests.cs) 的文档移动用例先将源文档浮动，移动完成后源浮窗关闭。真实鼠标用例同样覆盖浮窗回到主窗。新截图描述的是文档从仍然存活的主窗口进入已有浮窗，该时序没有对应回归。

另外，原测试正文 `ProbeView` 是空 UserControl，并且在关键阶段主动刷新布局，未刻意构造“旧窗口保留待安排子控件、新窗口测量尚未完成”的队列状态。此前 1039 项开发门禁通过及部署启动通过，不构成这一跨窗口时序已覆盖的证据。

## 修复与回归方向

修复应围绕跨窗口 View 交接与失效布局任务处理，不改变区域合并语义，也不以捕获后忽略异常来假装成功。

- 先加入主窗到已有浮窗的回归：使用真实正文子树，保留源窗口，构造待处理布局，检查原 Document/View/Scope 仍唯一且未释放。
- 分别验证先处理旧窗口和先处理新窗口的调度顺序，并覆盖最后一项与非最后一项、普通文本与复杂页面、反向回停和连续往返。
- 优先评估宿主的跨 TopLevel 交接边界能否保证安全时序；不能在模板或布局回调中无条件增加 `UpdateLayout`，需处理重入及延迟内容创建。如果宿主无法可靠控制，应在固定 Avalonia 补丁中让旧布局队列跳过已离开本窗口的控件，并增加框架级回归。
- 最小复现通过后还需宿主输入链路、完整本地开发门禁和实际桌面验收，才能编译部署修复版。

本轮未使用 AIFLOW，未运行 Windows CI 或发布门禁；安装文件及用户插件保持原状。
