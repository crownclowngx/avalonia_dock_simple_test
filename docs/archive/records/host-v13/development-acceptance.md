# V13 插件启用与禁用开发记录

> 后续确认（2026-09-20）：当前 Host 人工验收已由项目所有者实际手工使用后[确认通过](../host/manual-acceptance-20260920.md)；历次本机交付见[部署索引](../../../maintenance/local-deployment.md)。下文与相邻 JSON 保留原阶段事实，“未执行／待验收／未部署”均指当时状态。外部业务与发布事项由[现行待办](../../../roadmap/README.md)继续跟踪。

> 日期：2026-09-19；分支：master；起点：58d7bee8957f0fc54ae69e412b6ee45ae7457556 及已有未提交修改。
> 实现、专项及文档已完成；Markdown 定稿后执行最终本地 verify，最终判定、计数与源码/产物身份仅回填 [final-development-evidence.json](final-development-evidence.json)。本页不代替最终门禁结果。
> 入口：[实施计划](../../plans/host-v13-plugin-enablement-plan.md)、[行为契约](../../../reference/plugin-enablement.md)、[专用验证](../../../maintenance/host-v13-plugin-enablement-verification.md)。

## 实际交付与设计审查

| 阶段 | 交付 |
| --- | --- |
| P0 | 基线 verify 通过；保存原有差异清单、SHA-256、二进制 diff 与原件，提交仅包含本轮改动 |
| P1 | 不可变设置、严格原子存储、窄状态/操作端口与串行意图服务 |
| P2 | HostRuntime 注入设置；清单候选保留；身份去重后在 DLL 加载前过滤；按插件根和数据根缓存首次启动事实 |
| P3 | 插件看板概览开关、当前/下次/待重启状态、筛选、失败回退、重读冲突设置、退出与关窗寿命保护 |
| P4 | 未加载产物磁盘检查、布局和收藏保留、工作流缺失动作、三次独立进程重启探针 |
| P5 | 用户指南、内部设计、兼容约束、专用契约/验证/归档同步；完整开发门禁结果见 JSON |

SRP：存储、意图、发现、查询和交互分别拥有变化原因。OCP：所有插件共用 PluginId 策略，无业务插件特判。LSP：启用插件继续经过原加载、隔离和生命周期协议；查询仍无副作用。ISP：读状态与写意图端口分开，SDK 不扩张。DIP：界面依赖窄端口，发现接收冻结数据，组合根负责连接。采用 sealed 类、不可变记录、SemaphoreSlim 和既有 MVVM 命令，没有新增通用框架。

中文注释覆盖提交点、备份只读、未来格式、跨实例修订、缓存、禁用过滤以及迟到回调。没有修改 SDK API、发布阈值、测试分类或 Gate 实现。原工作树已有版本/框架/布局/Gate 修改属于基线，完整门禁验证的是包含这些修改的工作树，不能只用 HEAD 代替源码身份。

## 实际专项

基线 `artifacts/gate/20260919-072501-58d7bee8957f/summary.json` 通过：SDK 91、Host Unit 528、Plugin 255、UI 220、MyPlugTest 11、ZIP 1；均无失败或跳过。

| 专项 | 通过 | 失败/跳过 | TRX |
| --- | ---: | --- | --- |
| 设置、查询、布局、收藏、工作流内核 | 100 | 0/0 | artifacts/host-v13/unit/v13-unit.trx |
| 加载、隔离、生命周期、重启与工作流 | 77 | 0/0 | artifacts/host-v13/plugin/v13-plugin.trx |
| 看板、工具中心、布局与导航 Headless | 37 | 0/0 | artifacts/host-v13/ui/v13-ui-final.trx |

专项彼此复用测试，不可相加作为唯一测试总数。最终 verify 重新执行当前源码的全部开发测试。

重启夹具复用现有 Plugin 测试程序集，子进程只执行受控入口，依次加载→禁用→重新启用，同一隔离数据根、三个不同 PID。禁用阶段枚举所有 ALC 的程序集位置，验证无 DLL；同时无构造/Configure/初始化/关闭/释放探针，六种贡献均为零。启用阶段每种贡献一项且六个探针按顺序完成。父进程检查退出码、超时与收据，不给生产 Host 增加测试后门。

Headless 使用实际窗口、绑定、鼠标与空格键按下/释放、持久化文件、Dispatcher 和深浅主题渲染。窄窗口截图存于 UI 测试输出 `TestResults/v13-ui`。这是自动化界面证据，不能代替原生桌面体验。

开发中修正过测试夹具问题：无效 ID 示例原本合法；加载 DLL 后 Windows 临时目录清理被锁；探针 schema 缺 properties；ToggleSwitch 点击需落在轨道、空格需同时释放。失败记录保留在 artifacts/host-v13，最终结果指向修正后的 TRX，没有通过删除断言或跳过获取绿灯。

## 矩阵到测试的索引

逐个实际测试名、结果和 TRX 摘要存入最终 JSON。以下说明复用关系，避免为同一稳定协议复制测试。

| 编号 | 实际测试类/行为 |
| --- | --- |
| S01–S06 | PluginEnablementSettingsTests；数据根规则复用 HostDataRootPolicyTests；原子提交后无后续 I/O 另经代码审查 |
| S07–S09 | PluginEnablementServiceTests、PluginEnablementQueryTests、PluginEnablementLoadingTests；串行、未知身份、冲突和无副作用查询 |
| L01–L05、L07 | PluginEnablementLoadingTests、ManagedOnlyPluginLoadingTests、PluginManifestCompatibilityTests；混合目录、改名、重复身份及坏配置 |
| L06、L08–L09 | PluginEnablementQueryTests、PluginEnablementUiTests、PluginEnablementRestartTests；原隔离和退出取消由 PluginContainerIsolationTests、HostLifecycleOwnershipTests 与现有 UI 退出测试回归 |
| Q01–Q04 | PluginEnablementQueryTests、PluginStatusTests、PluginDashboardTests；候选合并、四态、筛选和脱敏导出 |
| U01–U06 | PluginEnablementUiTests、PluginStatusWindowTests；真实绑定、只读、保存关窗、重开、键盘、主题、空/禁用目录下管理窗口 |
| X01–X02、X04 | PluginEnablementRestartTests；新进程贡献与工作流目录；禁用提供方请求返回原缺失码 |
| X03 | PluginEnablementRetentionTests、DockLayoutTreeTests、DockLayoutWorkspaceStateTests、ToolCenterTests 与 DockLayoutV3UiTests；数据往返及原恢复协议 |
| X05 | PluginEnablementLoadingTests.禁用项检查磁盘产物不加载程序集且保留启动事实，以及 PluginCompatibilityEvidenceTests、PluginDashboardTests |
| X06–X07 | 新进程探针保持本次启动快照；既有生命周期、关闭取消、Provider/Scope、SDK/API 与真实包兼容门禁回归 |

## 未执行范围

M01–M05 原生桌面检查全部未执行：当前没有可用的原生桌面控制能力。没有把 Headless、进程探针或历史业务报告标记为原生验收。外部旧插件冻结二进制和真实业务没有新增实测；本仓现有真实 DLL、隔离和 ZIP 兼容验证正常保留。

没有运行 AIFLOW、Windows CI、seal、发布 Windows Smoke、发布覆盖率或重复性门禁；没有部署安装目录、上传包或发布。测试构建写入自身 bin/Controls 的夹具属于开发验证。原工作区差异保留；提交只涉及 V13，原改动不会被顺带提交。
