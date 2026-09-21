# V20 历史布局退役与入口收敛开发记录

> 日期：2026-09-21。代码、专项和文档已落地；最终完整开发门禁、源码身份与输出以[非嵌入证据](development-evidence.json)为唯一状态来源。本记录不是部署或发布证明。

## 1. 结果与兼容边界

Layout V2 的模型、编解码、校验、Store、Mapper 和转换器已整体退出。V3 Store 只读取当前主文件与备份，没有可用 V3 时由生命周期建立默认全隐藏工具布局。`layout-v1.json` / `layout-v2.json` 不探测、不读取、不迁移、不修改，不新增旧文件清理器。

这项有意变化由所有者明确授权：只拥有旧 V2 文件的用户不再恢复旧布局。V3 的原子提交、上一有效备份、坏文件证据、未来格式与 I/O 只读保护保持；产品/程序集/SDK 版本、默认 v2 数据根、manifest schema 2 和 Document Envelope V2 不变。

有效 Dock 测试已先迁到真实 V3 Store/Workspace，再删除纯旧协议测试。详细断言与删除理由见[逐方法测试去向](test-matrix.md)。旧文档创建分组查询已删除，分类树和精确创建身份统一由现行目录表达。Workflow 调查、Manifest 整理未纳入本轮。

## 2. SOLID 与设计审查

- Session 仍拥有工作区和实例；LayoutState 处理树和恢复记录；Store 拥有文件与写锁；Queue 拥有保存调度。没有第二状态源、新 Manager、迁移框架、版本注册或清理服务。
- `DockLayoutFormatException` 从旧 reader 文件归位，稳定错误码和安全消息边界保持。`RetiredHostToolIds` 仍被 V3 Merge 与工具偏好使用，不能按名称随 V2 删除。
- Gate 仅提取本类内小型产物检查方法，发布入口与单测共用；完整格式校验仍属于 Host。Smoke 新建产物目录拒绝旧文件，与用户已有数据根忽略旧文件是不同职责。
- 原位恢复中的 Tool/Model/View 身份、Document Scope、关闭否决及失败回滚由现行单元和 Headless UI 断言保护。没有改造依赖注入、SDK 或插件激活协议。
- Store、共享异常、测试输入、脚本进程与 Gate 检查均补充中文设计说明。覆盖率清单仅移除两个已删除 V2 文件；其余文件阈值及整体 84.39%/70.58% 不变，本轮未执行发布覆盖率。

## 3. 开发验证过程

实施分支 `codex/host-v20-layout-retirement`；代码基线 `661ac7e`，方案提交 `20e8121`。完整 P0 `verify` 在干净的 `20e8121` 上通过，run-id 为 `20260921-084050-20e812160548`：SDK 91、Host Unit 658、Plugin 292、Headless UI 289、MyPlugTest 11、ZIP 验收 1，共 1342 项，无失败或跳过，Release 构建零警告。

| 阶段 | 实际验证 | 结果 / 证据目录 |
| --- | --- | --- |
| 先迁移有效行为 | Plugin 59、自动收起/工具中心 UI 17、数据根 Unit 6 | 均通过；`artifacts/host-v20/p1-migration/` |
| V2 退役 | Unit 89、Plugin 92、Headless UI 119 | 均通过；`artifacts/host-v20/p1-retirement/` |
| 工具适配 | Gate Unit 83（含新增产物检查 15 组） | 通过；`artifacts/host-v20/p2-tools/gate-unit-rerun/` |
| 跨进程写锁 | 10 项检查，实际 V3 读写及上一有效备份 | 通过；`artifacts/host-v20/p2-tools/writer-lease.json`，包含被测 DLL SHA-256 |
| 目录收敛 | Unit 73、Headless UI 27 | 均通过；`artifacts/host-v20/p3-query/` |
| 文档与最终完整输入 | 帮助专项、全仓文件链接/GitHub 锚点、实际嵌入读取与渲染、最终完整 `verify` | 实际结果与最终源码/DLL 身份见同目录 JSON；不预填最终通过数 |

所有构建/测试均串行使用 `-m:1`。完整命令、时间、退出码、TRX、输入指纹、失败和重跑由 JSON 保留；专项可复用命令见[专用维护指南](../../../maintenance/host-v20-layout-retirement-verification.md)。测试总数下降由纯兼容测试退出解释，不使用无意义测试补齐历史数量。

初次迁移 Plugin 专项 56 通过、3 失败：两项把未测量运行时权重 0.2 当作 V3 归一化占比，实际为 `0.2/(1+0.2)=1/6`；一项沿用旧树的嵌套 Root 查找隐藏集合。修正测试对现行 V3 的观察位置后 59 项通过，没有修改生产恢复行为迎合旧断言。Gate 首次构建漏配测试类私有临时目录助手，补齐后 83 项通过；编译失败不计为测试通过。

## 4. 文档与历史维护

当前基线、Layout V3、架构、设计取舍、兼容约束、快速指南和导航同步。V20 方案归档为历史评审，专用验证转入 maintenance；旧 V2 reference 转入 archive/specifications，历史正文保留当时事实，仅附加退役说明和修正链接。删除源码的历史链接改为明确的当时路径文本。

八个帮助章节、四篇理论文章和架构入口保持。文档扫描区分 GitHub 锚点与实际帮助渲染器的既有中文锚点差异；该既有差异本轮只记录，不扩大到帮助渲染重构。最终 JSON 记录本轮链接及嵌入检查结果。

## 5. 未执行范围

未使用 AIFLOW、Windows CI、`seal`、发布 Windows Smoke、发布覆盖率或发布重复性门禁；未部署安装目录、上传包或创建发布标签。Writer Lease 只终止自己创建的持锁进程，测试只清理各自临时目录。

Gate 的发布产物检查已适配并自测，真实发布进程链仍留在[待办](../../../roadmap/README.md)。本轮开发验证不扩展既有人工验收，不授予发布资格。
