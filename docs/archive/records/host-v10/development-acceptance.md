# Host V10：插件兼容治理与看板开发记录

> 状态：实施中。日期：2026-09-16。方案见 [V10 计划](../../../roadmap/host-v10-plugin-compatibility-and-dashboard-plan.md)。
> 分支：`codex/host-v10-plugin-dashboard`，从 master 的 `9bceb0781fab` 创建。允许阶段提交；不使用 AIFLOW、Windows CI、seal 或发布流程，不修改用户安装目录或外部插件源码。

## G0：开发基线

创建分支时仅有本次对话编写的计划及两份导航修改，没有其他未提交变更。保留并在本阶段提交这些文档。

执行 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`，运行 ID `20260916-024435-9bceb0781fab`，locked restore、Release 零警告构建、契约、真实 ZIP 验收通过。

| 测试组 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- |
| SDK | 91 | 0 | 0 |
| Host Unit | 401 | 0 | 0 |
| Host Plugin | 218 | 0 | 0 |
| Headless UI | 117 | 0 | 0 |
| MyPlugTest | 11 | 0 | 0 |
| ZIP 验收 | 1 | 0 | 0 |
| 总计 | 839 | 0 | 0 |

证据位于 `artifacts/gate/20260916-024435-9bceb0781fab/summary.json`。这是含计划文档的工作树验证，不是新增功能验证。

旧插件输入从当前安装目录只读复制到 `artifacts/host-v10/baseline/Controls`，逐文件比较源与副本 SHA-256；清单见 [冻结输入](old-plugin-inputs.json)。副本仅用于后续验收，不能将本次复制记为新版兼容通过。

## 执行范围

G1–G8 尚在实施，后续在此追加真实结果。API 发布分类与 seal 的已知条件保持原状；开发验证不授予发布资格。

## G1–G2：规则与证据基础

唯一 XML 规则同时供 Host、MSBuild、独立打包脚本与 Gate 消费。保留原共享根、Icons 私有及历史禁带行为，修正 Build README 中禁带即共享的错误解释。

增加 Host 内部的产物指纹、只读元数据、报告校验、纯匹配和原子导入基础；不新增 SDK public API 或公共 NuGet 包。报告匹配区分产物、Host、规则、环境及未知状态。

验证：新增证据单元测试 10/10、既有依赖边界与隔离专项 17/17、Gate 自测 46/46；失败与跳过均为零。Host 与 MyPlugTest Release 构建零警告。完整验收在 G8 再执行。

## G3–G4：构建信息与独立验收

Build 从真实 project.assets.json 生成可选 `plugin.build.json`，部署与打包共同携带；不修改严格 manifest schema 2。新文件不包含时间或个人绝对路径，旧产物缺少它时显示未知。

新增 `tools/MyAvaloniaManagement.Compatibility`，按插件隔离输入与测试进程，核对 Host / UI 测试实际运行文件，严格检查 TRX 执行数量，输出分层报告。用法见[专项维护指南](../../../maintenance/plugin-compatibility-verification.md)。工具参数和 TRX 自测加入 Gate.Tests，阶段结果为 52/52；证据基础测试增至 12/12。

2026-09-16 的开发阶段重放：冻结输入 12 个插件静态与加载组合全部通过；仅显式选择 ClassicGamePlugin 和 MyPlugTest，二者 Workspace 通过。其他 10 个 Workspace、全部业务与真机未执行。证据目录 `artifacts/host-v10/external-acceptance/20260916-030155-fccd91c1`。这是当时开发工作树的文件身份；后续 Host 实现变化后会重验，不能将该报告直接视为最终 Host 验收。

## G5–G6：证据查询与看板

已实现三页签、显式文件检查、报告导入与历史保留、复制和文本导出。窗口仍由 Runtime 拥有，不注册为 Dock Tool；运行状态与验收结果分离。后台任务有取消和代次控制，文件变化后不能用新磁盘身份替换已加载会话。

阶段验证：Host 状态、命令投影、证据和新查询专项 56/56；既有窗口 Headless 交互 4/4；浅色、深色、680×520 紧凑布局已渲染检查。最终真实 ZIP 到看板的贯通与完整 verify 留在 G8 记录。

规则冲突自检额外发现并修复旧正则漏过 `Avalonia.dll` / `Ursa.dll` 本身的问题。共享根不变，只补齐两个共享主文件的禁带行为；规则摘要变化，因此需以最终规则重验原冻结输入。候选 `.1` 为修复前实验，`.2` 为修复后的本地消费输入，不上传也不覆盖公共包。

## G7：发布事实与开发 API 比较

从 NuGet 官方平面源取得 Core、UI、Workflow 3.4.1，记录包与程序集摘要及当前 API 文本映射至 `build/ApiBaseline/3.4.1.json`。原 Shipped / Unshipped 文件均未修改。固定 ApiCompat 10.0.302，默认方向核对已发布到当前的兼容性，严格模式另记录是否表面相同。

`tools/Verify-PluginApi.ps1 -RunSelfTests` 在 `artifacts/host-v10/api-verification` 记录三个 SDK 均兼容且严格表面相同；三种工具夹具证明兼容新增通过、删除与改签名失败。正常新增在严格比较中被识别为差异。开发 verify 已接入该比较，正式 seal 行为保持原样。

本地候选 `3.4.2-v10.20260916.2`：六包使用相同实验标识、保持程序集身份 3.4.1.0（Workflow 仍为 1.0.0.0），不改正式集中版本。独立模板 hive、源码目录、feed、缓存完成生成、locked restore、零警告构建、4/4 测试、相同输入元数据稳定性与 ZIP 打包；结果在 `artifacts/host-v10/candidate-consumer-final`。

额外从 master 基线 `9bceb0781fab` 创建临时只读源码工作树，构建旧 Host 外部验收测试。对携带新构建信息的候选 ZIP，加载与组合 1/1 通过，证明旁路文件未破坏该旧 Host 消费；证据在 `artifacts/host-v10/old-host-new-package`。不把此项推导为任意旧 Host 或业务兼容。
