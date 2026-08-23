# Host V4 G6：文件系统路径语义与展示模型收口

> 状态：已完成（2026-08-23）。输入提交为 G5 `73b2fe4`；输出为承载本文、源码和测试的
> `refactor(host-v4): 完成 G6 路径语义与展示模型` 提交。G7–G8 仍待实施，V4 尚未封板、尚不可发布。

## 1. 路径单一事实源

`Models/FileSystem/FileSystemPath.cs` 现在只负责解析字符串，不访问磁盘或网络，并返回包含规范路径与
分类的不可变结果：普通绝对目录、本地驱动器根、UNC 共享根。行为已经固定为：

| 输入 | 规范结果 | 分类 |
| --- | --- | --- |
| `C:`、`C:\` | `C:\` | 本地驱动器根 |
| `\\server\share` 及尾分隔符形式 | `\\server\share` | UNC 共享根 |
| `\\server\share\folder` | 去除尾分隔符的绝对路径 | 普通目录 |
| 空白、相对路径、设备路径、非法路径 | 失败 | 不产生展示状态 |

`IHostStorageService.DirectoryExists` 是唯一存在性端口。ViewModel 不直接探测真实网络共享，因此测试能用
内存替身证明 UNC 行为；路径不存在或在选择过程中消失时，原有树、标题和驱动器模式保持原子不变。
本地驱动器根继续使用驱动器集合，UNC 共享根和其他用户选择目录则作为唯一自定义根展示。

## 2. 只读展示快照与命名对齐

- `CategoryNode` 在构造期复制 `CategoryName` 与 `Documents`，两者只读，仅 `IsExpanded` 可变；
- `PlugGroupMenuViewModel` 以非空字段持有依赖，构造参数与命令参数失败明确，不发布半成品分类；
- `AssemblyLoadConstant.PLUGINS_SUBDIRECTORY` 收口为
  `PluginDeploymentConstants.PluginsSubdirectory`，值仍为 `Controls`；
- 文件树刷新会恢复本地驱动器模式，选择、刷新、展开和 Document 创建均由行为测试保护。

## 3. SOLID 与朴素设计

- **SRP**：路径语法、存在性检查和 UI 状态提交分别由值解析器、存储端口和 ViewModel 负责；
- **DIP**：ViewModel 只依赖已有的 `IHostStorageService`，没有直接访问网络或增加服务定位器；
- **ISP**：只在真实存储边界增加一个 `DirectoryExists` 能力，没有建立通用文件系统 Facade；
- **OCP/LSP**：展示节点使用构造期快照，消费者不能从外部替换集合破坏分组不变量。

实现只使用值结果、构造防御和原子状态提交，没有引入策略层级、事件总线、Manager 或缓存框架。

## 4. 实际命令与结果

```powershell
pwsh -NoProfile -File .\scripts\Test-HostV4DevelopmentGate.ps1 -Stage G6
dotnet test .\Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj -c Release --no-restore --nologo
dotnet test .\Plugins\MyPlugTest\MyPlugTest.Tests\MyPlugTest.Tests.csproj -c Release --no-restore --nologo
dotnet test .\Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj -c Release --no-restore --nologo
dotnet test .\Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj -c Release --no-restore --nologo
dotnet test .\Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Release --no-restore --nologo
```

G6 门禁包含锁定还原、Release `-warnaserror`、结构/API/格式/文档检查及 Host 三层全量：Unit
**210/210**、Headless UI **63/63**、Plugin **205/205**，合计 **478/478**；Host 行/分支覆盖率为
**85.06% / 71.41%**，高于 G0 的 **84.39% / 70.58%**。`FileSystemPath` 行/分支覆盖率均为 **100%**，
其真实分类决策分支全部被路径矩阵覆盖。

最终非发布回归为 SDK **37/37**、MyPlugTest **11/11**、DaTangAccountingHelpPlug **62/62**、
MySmallTools **192/192**、BiliDownloader **728/728**。G4 已单独完成 MySmallTools 20 轮真实媒体
Harness，并证明弱引用、原生资源和加密流归零，本阶段不重复冒充 Windows Smoke。

## 5. 兼容、非发布与回滚

Plugin SDK public API 及 Shipped 127/45 未改变；manifest、Document envelope、layout schema、
`layout-v2.json`、数据根 `v2`、四插件版本/SDK 区间和 `Controls` 部署目录值均保持不变。

本阶段未使用 AIFLOW，未运行 Windows CI、Windows Smoke、ReleaseAcceptance、
`Invoke-HostV3ReleaseGate.ps1` 或其他发布门禁，未创建标签、上传或发布。机器摘要固定为
`aiflow/windowsCi/windowsSmoke/releaseAcceptance/releaseGate/publishable=false`。

回滚单位是 G6 的路径分类器、存储端口增量、展示快照、常量命名、测试与本文，整体回到 G5
`73b2fe4`。不得只恢复旧路径算法而保留与它矛盾的测试，也不得通过旧常量转发层制造第二事实源。
