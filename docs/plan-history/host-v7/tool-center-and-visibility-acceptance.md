# Host V7 工具中心与显隐改造实施记录

> 日期：2026-09-11。范围：本仓 Host、测试与文档；V7 开发版本，已按用户要求本地部署，未对外发布产品或 NuGet。
> 任务书：[V7 设计](../../design/host-v7-tool-center-and-visibility-plan.md)。
> 使用入口：[工具中心说明](../../quick-start/tool-center.md)。
> 结论：实现、完整 verify、Host 覆盖率、Headless Skia 界面验证及 Windows 发布产物启动检查通过；真实桌面验收边界见第 6 节，本地部署见第 7 节。

## 1. 实际基线与修改范围

代码基线为 `a337c114f463b2649c94c98b556fb3f64fb8cd48`。实施开始时已有本轮方案文档及两处文档索引改动。
用户随后授权按文档实施，因此本次包含生产代码修改，不再沿用最初仅讨论的任务范围。

首次 verify 在 locked restore 阶段失败：七个测试资产的 `packages.lock.json` 保留了与普通构建不一致的
win-x64/ILLink 依赖图，触发 NU1004。原始证据见
[基线失败](../../../artifacts/gate/20260911-070503-a337c114f463/summary.json)。
执行 `dotnet restore MyAvaloniaManagement.sln --force-evaluate --nologo` 修复测试资产 lock 后，原 Host 单元测试 350/350 通过。
七份 lock 修正保留在改动中；没有通过关闭 locked mode 绕过问题，后续两次完整 Gate 均使用 locked restore。

没有修改外部业务插件、SDK 公共签名、Shipped API 基线、产品版本、manifest schema 或布局 V2 线格式。
旧 SDK 3.3.0 测试资产增加了一个 `Prevent` Tool，用于真实二进制兼容检查。

## 2. 最终行为与职责

| 组成 | 实现职责 |
| --- | --- |
| `ToolCenterWindowService` | 主窗口 Owner、可缩放非模态窗口、重复打开/最小化恢复、关闭与退订 |
| Host Command、欢迎页、Palette | “工具 → 工具中心…”；工具搜索结果携带独立 ToolTypeId，执行时复核可用性 |
| `ToolWorkspaceReadModel` | 一次布局遍历，合并注册目录、来源、状态、图标 Owner 和可执行条件 |
| `ToolCenterQuery` | 全部/常用/最近/已显示/已隐藏/用途分类、全局搜索及来源约束、缺失历史项 |
| `ToolCenterActions` / `WorkspaceSession` | 统一打开与隐藏结果、成功访问记录、退出门控和批量最终通知 |
| `ToolCenterPreferences` / Store | 独立收藏顺序、20 项最近记录、用户分类、原子保存、坏文件与未知版本处理 |
| Dock Adapter / Coordinator | 所有 Tool 允许隐藏，恢复复用模型和 View，自动收起项预览与定位 |
| Layout Lifecycle / Migration | 新工具默认隐藏；定向删除旧管理 ID，恢复有效布局，覆盖前保留原文件备份 |

旧 `ToolManagementView`、ViewModel、Item、Host 注册和“最后创建管理工具”分支已经删除。
工具中心没有登记成 Tool 或 Document；全部隐藏后恢复入口仍在主菜单中。
分类与收藏不更改 Dock 位置；工具隐藏不释放插件 singleton，业务任务仍由插件自己的生命周期管理。

状态和偏好分别保留在 Workspace 与 Runtime 服务中，窗口模型只负责当前选择、筛选和命令反馈。
偏好写入失败不撤销本次内存修改；刷新使用 Dispatcher 合并通知，关闭后拒绝迟到回调和命令。
旧 `Prevent` 枚举继续存在，V7 Host 的 Adapter 不再使用它禁止隐藏。

## 3. 开发门禁与覆盖率

最终生产代码验证命令：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

[最终 Gate 记录](../../../artifacts/gate/20260911-074307-a337c114f463/summary.json) 为通过。
Release 构建启用 `-warnaserror`，结果为零警告、零错误；还包含契约检查、MyPlugTest ZIP 打包与真实包加载验收。
此前 [首次绿色 Gate](../../../artifacts/gate/20260911-073956-a337c114f463/summary.json) 同样通过；
最终一次包含欢迎页最近访问统一入口、窗口关闭后的命令保护、线程刷新合并和批量失败 UI 测试。
之后只更新文档并对同一 Release 产物补采覆盖率。

| 测试组 | 最终通过数 |
| --- | ---: |
| SDK | 91 |
| Host Unit | 362 |
| Host Plugin | 207 |
| Host UI | 86 |
| MyPlugTest | 11 |
| 真实 ZIP PackageAcceptance | 1 |
| 合计 | 758 |

普通直接执行 PluginTests 时，包验收测试因未提供 Gate 生成的包目录而失败过；这是包测试的前置条件，
没有删除或跳过该验收。最终 Gate 使用自己生成的 ZIP 提供目录后 1/1 通过。

当前 verify 不采覆盖率。本轮按现有 `Host/MyAvaloniaManagement.Tests/coverage.runsettings` 分别采集 Unit、
Plugin、UI 和包验收四份 Release 报告；前三份使用 `Category!=PackageAcceptance`，第四份使用 Gate 生成的
`MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT`。每份使用 `--no-build --no-restore --collect 'XPlat Code Coverage'`。
ReportGenerator 只合并原始 GUID 目录报告，程序集过滤为 `+MyAvaloniaManagement`，未排除新工具中心类型。

| Host 覆盖率 | 实测 | 当前门槛 | 结果 |
| --- | ---: | ---: | --- |
| 行 | 87.67% | 84.39% | 通过 |
| 分支 | 72.04% | 70.58% | 通过 |

证据：[合并摘要](../../../artifacts/v7-tool-center/coverage-final/merged/Summary.txt)、
[Cobertura](../../../artifacts/v7-tool-center/coverage-final/merged/Cobertura.xml)、
[按 Gate 配置判断的阈值结果](../../../artifacts/v7-tool-center/coverage-final/threshold-result.json)。

## 4. 重点回归结论

| 场景 | 证据与结果 |
| --- | --- |
| 内建 Tool 均可隐藏、恢复 | Unit 验证无管理项、全部 CanClose、重复显隐、实例保持和一次提交通知 |
| 旧 SDK Prevent | 已发布 UI SDK 3.3.0 编译的 DLL 经真实 Loader/Provider 组合，保留 Prevent 声明、模型数据与可关闭 Adapter |
| 外部 Tool 显隐与所有权 | UI 使用真实 PluginProviderOwner 组合 Prevent 模型；隐藏不 Dispose、不创建重复页面 |
| 大目录 | 100 个插件 Tool 加 3 个内建 Tool；真实 ListBox 虚拟化、同名身份区分、ID 搜索、浏览期间实例计数不增加 |
| 搜索/分类/常用/最近 | 全局搜索、来源约束、清空恢复、计数、稳定分类 ID、删除回退、收藏顺序、最近去重及上限 |
| 不可用与失败 | 从工作线程撤回插件可用性，保留说明并禁用打开；旧快照不能绕过操作复核；已有实例可隐藏 |
| 批量部分失败 | 模拟 Dock 拒绝一项，其他项成功隐藏，反馈失败数及名称；不受搜索限制，不关闭 Document |
| 偏好异常 | 损坏/空 JSON 备份、未知版本只读、非法与重复字段归一、写入失败保留内存、观察者异常隔离 |
| 独立窗口 | 非模态、Owner 可操作、单实例、最小化恢复、Esc、连续开关、Owner 关闭、DataContext 及订阅释放 |
| 自动收起 | 使用实际 Dock Factory 预览目标；仍保持 pinned 集合身份，隐藏/恢复不丢失实例 |
| 迁移 | 管理项隐藏/显示/pinned、仅管理项、迁移幂等、相对顺序/比例保持、原始字节备份；普通未知项继续严格拒绝 |
| 启动 | 新布局全隐藏、有效旧快照恢复、快照未含新工具隐藏、全隐藏保存后重启和重新显示 |
| 既有功能 | V6 文档功能中心、插件目录双模式、V6.1 图标 Owner 边界、原 Command 上下文和退出保护随完整套件通过 |

## 5. 视觉证据

通过生产 App 资源、真实 XAML 和 Headless Skia 渲染检查了以下图像：

- [浅色窗口](../../../artifacts/v7-tool-center/tool-center-light.png)
- [深色窗口](../../../artifacts/v7-tool-center/tool-center-dark.png)
- [720×600 窗口](../../../artifacts/v7-tool-center/tool-center-compact.png)
- [103 项工具目录](../../../artifacts/v7-tool-center/tool-center-100-tools.png)

列表和分类独立滚动，工具名称截断时提供提示，主要操作不依赖侧边纵排长标签。
截图取自专属 UI 测试；不是替代产品界面的静态设计稿，也不是 Windows 桌面截图。
可通过 `MYAVALONIA_V7_RENDER_DIRECTORY` 和 `ToolCenterUiTests` 测试重新生成。

## 6. 验收边界与回退

G0–G5 的实现和对应自动化回归已完成。G6 的开发门禁、覆盖率、文档和 Headless 视觉检查已完成；
以下真实桌面场景本次没有验证，因此不把整个桌面发布验收标记为通过：

- Windows 多显示器、100%/150%/200% DPI 切换和原生窗口前后层级、实际键盘焦点体验。
- 外部 Bilibili/闲才插件带真实在途任务时的显隐联调。自动化验证的是 Host 不释放模型，未执行这些插件的真实业务。
- 真实桌面拖拽停靠、主窗口脏文档关闭取消期间与工具中心连续操作的人工体验。

没有运行 seal 或外部插件发布；本次 Gate 的 `releaseEligible`、`publishable` 均为 false。
后续本地部署补做了隔离目录中的 Windows 发布产物启动检查，未包含已安装的外部插件，不替代上述人工验收。
既有有效布局会恢复；首次启动全隐藏的变化可以通过工具中心随时调整。
回退代码时可使用迁移前备份恢复旧布局，保留独立偏好文件；不要把旧管理 Tool 重新注册成与新中心并行的入口。

## 7. 本地主程序部署（后续授权）

用户随后要求编译主程序并发布到 `D:\data\avalonia`。2026-09-11 16:03（Asia/Shanghai）完成本地部署。
从当前源码创建隔离快照后执行 Release、win-x64、自包含、压缩单文件发布，关闭裁剪并跳过插件部署。
RID 发布所需的 restore 在快照中执行，没有改写工作区的锁文件。

| 项目 | 结果 |
| --- | --- |
| 目标程序 | `D:\data\avalonia\MyAvaloniaManagement.exe` |
| 发布产物 | `artifacts/host-deployment-20260911-155817/publish` |
| 覆盖前备份 | `D:\data\avalonia-host-backups\20260911-155817` |
| 主程序文件 | 更新 150 个文件，逐项 SHA256 校验通过 |
| 已安装插件 | 11 个目录、514 个文件，部署前后 SHA256 一致 |
| 启动检查 | 隔离数据目录运行实际 Windows 发布程序，退出码 0；生成 schema 2 布局，3 个内建 Tool 均隐藏，无旧管理 Tool |
| EXE SHA256 | `9AD8C43C0977EB848CFFCFDF2CE15FE40E878B34D3FF64426EA4D58C93A48B05` |

部署仅复制本次主程序发布文件，其他既有文件保留；未启动目标目录中的业务实例。
该操作为用户授权的本地部署，不代表正式 seal 或对外发行完成。
证据：[部署结果](../../../artifacts/host-deployment-20260911-155817/deployment-result.json)、
[启动检查](../../../artifacts/host-deployment-20260911-155817/smoke-result.json)、
[文件校验清单](../../../artifacts/host-deployment-20260911-155817/deployment-files.json)。
