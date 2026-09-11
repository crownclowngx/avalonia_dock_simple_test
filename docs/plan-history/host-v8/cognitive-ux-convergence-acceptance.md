# V8 交互收口与认知减负实施记录

> 日期：2026-09-11。
> 状态：实施中；当前仅 G0 开发基线通过，不代表 V8 完成。
> 范围：本仓 Host、测试与文档；不使用 AIFLOW、Windows CI、seal 或发布 Windows Smoke，不上传或部署，`publishable=false`。

## 1. 方案与源码基线

- [V8 方案](../../design/host-v8-cognitive-ux-convergence-plan.md)已由用户确认并授权实施及阶段 Git 提交。
- 实施前 HEAD：`09ce9c2`。工作树仅有上一轮生成的 V8 方案和文档导航，无未提交生产代码修改。
- .NET SDK：`10.0.302`；产品 `3.0.0`，Core/UI SDK `3.4.0`，本轮不提升版本。

## 2. G0 本地开发基线

执行：`dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`。

[Gate summary](../../../artifacts/gate/20260911-134255-09ce9c2d235b/summary.json)：完整 verify 通过，Release build 零警告、零错误。

| 测试组 | 通过 | 失败／跳过 |
| --- | ---: | --- |
| SDK | 91 | 0／0 |
| Host Unit | 386 | 0／0 |
| Host Plugin | 211 | 0／0 |
| Host UI | 91 | 0／0 |
| MyPlugTest | 11 | 0／0 |
| 真实包验收 | 1 | 0／0 |
| 合计 | 791 | 0／0 |

verify 未采集覆盖率，最终实现后另采四份 Host 报告并校验阈值。该基线不包含新增 V8 行为、人因任务观察或发布验证。

## 3. 后续证据

G1–G6 的实现、测试、视觉、覆盖率与任务观察按实际完成情况追加。没有真实参与者的任务观察必须保留“未完成”，不能以自动化测试代替。
