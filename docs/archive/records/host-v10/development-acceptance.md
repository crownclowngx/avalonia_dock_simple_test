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
