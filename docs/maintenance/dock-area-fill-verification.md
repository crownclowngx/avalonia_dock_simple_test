# Dock 区域默认居中停靠专项验证

> 用途：维护区域回停实现与本地开发门禁。核对日期：2026-09-17。实现记录见[专项记录](../archive/records/dock-area-fill/development-acceptance.md)，最终运行结果见[非嵌入证据](../archive/records/dock-area-fill/development-evidence.json)。本轮不使用 AIFLOW、Windows CI、seal 或发布 Smoke。

## 行为边界

2026-09-17 已确认“Document 从存活主窗拖入已有浮窗”的布局崩溃，详见[诊断记录](../archive/records/dock-area-fill/cross-window-layout-crash-20260917.md)与[修复方案](../roadmap/host-document-cross-window-layout-crash-fix-plan.md)。下列既有自动化通过记录不代表该故障已修复；新回归需要覆盖旧窗口布局任务尚未完成的时序。

鼠标在合法 DocumentDock / ToolDock 正文内、没有指向明确按钮时，默认选择 Fill。已有标签保留，内容合并到目标组；只有空组才表现为填满空区域。不能把“填满”解释为覆盖已有页面。局部四向和外侧全局按钮保留分屏，明确可见按钮被拒绝时不回退成 Fill。标签栏保持排序/插入协议。

预览不改变模型。松开时重新定位目标并检查当前能力、可见性和全屏限制；关闭/离开/停用目标或取消拖动时清理提示。原生浮窗入口的移动事件传入窗口位置，松开事件传入客户区像素偏移，两者不能直接混用。失效窗口从排序候选中剔除。

Host 的 `DockSplitPolicy` 由预览与提交共同使用：Tool 对主文档区域上/下分屏遵守已有全宽策略，对 Tool 和浮窗内分屏保持局部。已有全宽稳定组使用其当前范围作为预览，新组按默认比例提示，实际像素尺寸仍受布局测量及分隔条影响。

## 开发入口

在仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
dotnet restore MyAvaloniaManagement.sln --locked-mode
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~DockAreaFill|FullyQualifiedName~DockToolSplit|FullyQualifiedName~DockToolWindowClose|FullyQualifiedName~DockPointerCapture|FullyQualifiedName~DockLayoutV3'
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

verify 的首阶段 `dock-patch` 负责补丁准备，失败会阻断 restore 和所有后续阶段。构建完成后，门禁核对 Host 与测试输出中的 Dock DLL 是否与包内 DLL 字节相同，拒绝增量复制留下的旧文件。主仓完整测试、SDK API 比较、MyPlugTest ZIP 验收继续沿用原门禁。没有运行任何 Windows 发布门禁。直接构建 Host 前先准备本地补丁源；外部插件无需引用补丁包。

## 自动化覆盖

| 层级 | 主要断言 |
| --- | --- |
| 固定上游补丁测试 | 开关默认关闭、正文/外部边界、标签栏排除、祖先裁剪、空组、非法根目标、能力及可见性、局部和全局方向优先、拒绝不合并、预览矩形与清理 |
| 两条状态机入口 | 悬停不执行，最终落点变化，关闭/停用/权限失效，捕获丢失，单次提交，原生浮窗坐标协议 |
| `DockAreaFillUiTests` | 生产样式启用、单项/整组文档跨窗合并、原模型/View/Scope、源空窗关闭、活动项与顺序、工具回停 Pin 和 V3 重启、全宽预览、全屏限制，以及真实标签鼠标事件闭环 |
| 原有 Host 回归 | Tool 上下分割、最后工具浮窗关闭与取消、捕获移交、布局保存恢复、生命周期和 Plugin SDK 兼容 |
| Gate 工具测试 | 补丁验证失败必须先于 restore 阻断；构建 DLL 与包不一致或缺失时拒绝；开发图不进入覆盖率或 windows-smoke |

所有测试报告必须有实际通过数，失败和跳过分别记录。框架入口测试以记录提交验证决策；Host 测试另验证真实移动后的资源与布局结果，二者不能单独替代另一层。

## 无个人缓存验证

使用新的独立终端，设置仅该进程有效的环境变量，避免影响平时的缓存：

```powershell
$env:NUGET_PACKAGES = Join-Path $PWD 'artifacts/dock-area-fill/isolated/nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PWD 'artifacts/dock-area-fill/isolated/http-cache'
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1 -ForceRebuild
if ($LASTEXITCODE -ne 0) { throw '补丁独立重建失败' }
dotnet restore MyAvaloniaManagement.sln --locked-mode --no-http-cache
if ($LASTEXITCODE -ne 0) { throw '独立缓存 locked restore 失败' }
```

第一次验证前目录必须不存在或为空；不要把复用旧目录说成无缓存验证，也不要删除用户全局缓存。SDK、SourceLink、补丁、NuGet 规范化和强名称处理详见[补丁维护说明](../../patches/dock-area-fill/README.md)。锁文件变化需要人工审阅是否仅涉及补丁依赖。

## 真实桌面及证据

原生桌面矩阵继续使用[实施计划第 8 节](../roadmap/host-dock-area-fill-implementation-plan.md#8-真实桌面验收矩阵)：中央按钮被遮挡、整窗原生拖动、标签排序、取消、混合浮窗、多屏负坐标、100%/150%/200% DPI、WebView/视频。当前会话无法使用原生桌面输入工具，这些项记为未执行；Headless 的窗口坐标不等同于操作系统窗口定位。

本轮不额外启用浮动提示窗口。区域判定已经支持正在拖动的源窗覆盖目标，但提示是否需要提升窗口层级，应由上述桌面结果决定。

文档会参与 Host 嵌入资源。先定稿 Markdown，再执行最终 verify；最后把命令、退出码、TRX 计数、源码输入清单、补丁/包/DLL 哈希写入非嵌入 JSON。修改嵌入文档后需要重新构建和更新证据，不能继续引用旧 DLL 哈希。
