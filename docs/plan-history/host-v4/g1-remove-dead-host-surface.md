# Host V4 G1：删除无行为价值的 Host 死面与依赖

> 状态：已完成（2026-08-23）。本阶段是 Host internal 删除性重构，不建立发布资格。

## 1. 完成事实

G1 同时删除了空拖放协议、主视图无消费者的 AllowDrop、空 DragOver/Drop、主窗口未使用的
Document 菜单查询依赖，以及文件菜单末尾的悬空分隔线。删除是按完整调用链进行的，没有留下
只删 XAML、只删接口或只改构造函数的半完成状态。

Host 不再直接引用 `Microsoft.Extensions.Hosting`；集中版本项和五份受 Host 项目引用影响的锁文件
同步刷新。实际使用的 `Microsoft.Extensions.DependencyInjection` 保持不变，容器仍以
`ValidateScopes=true`、`ValidateOnBuild=true` 建立。

## 2. SOLID 与朴素设计

- **SRP**：`MainWindowViewModel` 只保留真实绑定、布局、主题和 Document 操作协调。
- **ISP**：删除没有任何行为的拖放接口，而不是为两个空方法保留协议面。
- **DIP**：删除未读取的构造依赖，组合根只列出对象正确运行真正需要的服务。
- **OCP/LSP**：现有菜单命令、Dock、插件和 SDK 契约没有变化。

本阶段只做删除，没有引入替代接口、兼容包装器或新的 NuGet 包。未来若实现文件拖入打开，应作为
独立 UI 功能定义输入、错误和安全语义，而不是恢复本次空协议。

## 3. 验证与门禁

正式开发期入口为：

```powershell
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G1
```

入口执行锁定还原、Release `-warnaserror`、Host Unit/UI/Plugin、覆盖率、结构扫描和文档门禁。
结构负例证明生产 C#/XAML/项目文件中不存在 `IDropTarget`、`DragDrop.AllowDrop`、Hosting 直接依赖
或文件菜单尾部分隔线。实际测试数量和覆盖率以本阶段生成的
`artifacts/test-results/HostV4/G1/summary.json` 为准。

## 4. 兼容、非发布与回滚

Plugin SDK public API、四插件版本、manifest/Document/layout schema、`layout-v2.json` 和数据根 `v2`
均未变化。本阶段未使用 AIFLOW，未运行 Windows CI、Windows Smoke、ReleaseAcceptance 或 Host
发布门禁，未创建标签、上传或发布；摘要固定记录 `publishable=false`。

回滚必须把源码、XAML、组合根、项目引用、集中版本和锁文件作为整体回到 G0；不得只恢复空接口，
也不得只恢复 Hosting 的集中版本而形成虚假依赖事实。
