# Host V4 G2：收口强类型身份与用例入口

> 状态：已完成（2026-08-23）。本阶段破坏 Host internal 测试接缝，不修改 Plugin SDK public API。

## 1. 身份单一源

`HostExtensionIds` 继续拥有全部 Host Document/Tool 规范身份，重复维护裸字符串的
`DockNameConstant` 已删除。Welcome 的显示工具动作使用 `Action<ToolTypeId>`，Workspace 的
`ShowTool` 也只接受 `ToolTypeId`；只有进入 Dock Framework 字符串字典的最末边界才读取 `.Value`。

既有稳定字符串完全不变，因此 `layout-v2.json` 中的 Tool 身份不需要迁移。测试通过值对象和构造
参数反射证明 Welcome 不会重新退回 `Action<string>`。

## 2. 用例所有者与 Harness

`MainWindowViewModel` 删除了没有真实 XAML 消费者的 `CreateDocument(string)` 和
`OpenDocumentByPath(string)`；插件分组菜单删除字符串创建命令，只保留
`DocumentCreationMenuEntry` 强类型入口。选择文件打开、活动 Document 保存及错误条仍是主窗口的真实
UI 协调职责。

Host 持久化测试直接调用 `DocumentPersistenceCoordinator`，并把返回的
`DocumentOperationResult` 交给生产 `DocumentOperationState`，因此仍验证用户可见错误语义，但不再要求
ViewModel 为测试转发用例。

MySmallTools G3/G8 Harness 从自身已经拥有的 ServiceProvider 解析同一个 Coordinator。全部 Document
创建都显式 `await` 完成并检查失败结果后才遍历 Dock，删除了“发起异步命令后立即查找标签”的竞态。

## 3. SOLID 与朴素设计

- **SRP**：Coordinator 拥有创建/打开用例；ViewModel 只拥有真实绑定；Harness 只编排验收。
- **ISP**：调用方接收 `ToolTypeId` 或 `DocumentCreationMenuEntry`，不再接受语义宽泛的字符串入口。
- **DIP**：Welcome 依赖窄强类型动作，不依赖 Workspace、Dock 或服务容器。
- **LSP/OCP**：稳定 ID 值、布局内容和 Document 错误语义保持原样。

没有新增 public Harness API、兼容 overload、静态服务定位器或命令 Facade。

## 4. 验证、非发布与回滚

```powershell
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G2
pwsh -NoProfile -File .\scripts\Test-MySmallToolsV3.ps1 -Configuration Release -NoRestore -HarnessCycles 20
```

门禁覆盖 Welcome、Workspace、DocumentPersistence、强类型菜单入口、无转发方法负例，以及真实媒体
Harness 的异步创建和资源归零。Host 为 **457/457**，行/分支覆盖率为 **84.47% / 70.67%**；
MySmallTools 专项为 **687/687**，真实媒体 Harness 完成 20 轮，关闭后的 Document、View、加密流弱引用
与全部最终原生资源均归零，测试 ZIP 为 431 个文件。

本阶段未使用 AIFLOW，未运行 Windows CI/Smoke、ReleaseAcceptance 或 Host 发布门禁，未创建标签、
上传或发布，`publishable=false`。回滚必须整体回到 G1；不得恢复字符串入口或为 Harness 新增 public API。
