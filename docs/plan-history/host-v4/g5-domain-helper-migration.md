# Host V4 G5：按领域迁移 Helpers

> 状态：已完成（2026-08-23）。输入为 G4 提交 `13530dd`；本阶段是 internal 结构破坏性迁移，不提供旧命名空间兼容层。

## 1. 领域归位

`Business/Helpers` 已完全删除，原有类型按真实职责归位：

| 目标领域 | 内容 |
| --- | --- |
| `Business/Composition` | `HostRuntime`、`ServiceCollectionExtensions`、组合诊断异常 |
| `Business/Plugins/Discovery` | manifest、目录布局、ALC、预检、加载快照与共享程序集策略 |
| `Business/Plugins/Registration` | Registry/Builder、注册上下文、Provider 所有者、提交保护与激活边界 |
| `Business/Documents/Ownership` | Document Lifetime、Scope Manager 与所有者路由 Registry |
| `Business/Docking` | `DockDocumentLifetime` 与 `DocumentControlRecycling` |
| `Business/Workspace` | `DocumentCreationMenuQuery`（原 `PluginMenuService`） |
| `Models/FileSystem` | `FileSystemPath`（本阶段仅机械改名，算法保持不变） |

此外，Hello 视图/模型目录和命名空间统一为 Welcome；模型文件改为
`Models/Tools/ToolWorkspaceState.cs`，文件名与内部类名对齐。

## 2. 同步面与负例

生产代码、Host 三套测试、MySmallTools Harness friend 引用、XAML `x:Class`/命名空间、
覆盖率关键文件、专项脚本路径、文档门禁符号路径和当前架构链接均已更新。覆盖率阈值只移动键，
没有降低数值。

G5 结构门禁明确拒绝：

- `Business/Helpers` 或替代性 `Common/Utils/Misc` 杂物目录；
- `MyAvaloniaManagement.Business.Helpers` 及旧 Hello 命名空间；
- 旧路径转发类、type-forwarder 或 Plugin SDK 反向引用 Host 目录。

## 3. SOLID 与设计思路

- **SRP**：目录与命名空间直接表达类的单一职责，不再以 Helpers 隐藏不同变更原因。
- **DIP**：Composition 可以依赖各领域具体类，各领域不反向依赖组合根；仅组合诊断值对象作为显式边界。
- **ISP/OCP**：没有为迁移创建空接口或兼容 Facade；后续路径语义只修改 `FileSystemPath`。

这是文件、命名空间和 using 的机械归位，没有在本阶段偷渡 G6 路径行为。

## 4. 实际验证与回滚边界

```powershell
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore -warnaserror
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G5
```

锁定还原、Release `-warnaserror` 构建、Host 三层测试与覆盖率、负例结构扫描和文档链接均通过。
Host 为 **461/461**（Unit 193、UI 63、Plugin 205），行/分支覆盖率为 **84.80% / 71.18%**，
构建 **0 警告 / 0 错误**。

本阶段未使用 AIFLOW，未运行 Windows CI/Smoke、ReleaseAcceptance 或 Host 发布门禁，未创建标签、
上传或发布，`publishable=false`。回滚必须整体回到 G4，不得恢复转发命名空间；路径算法变更属于 G6。
