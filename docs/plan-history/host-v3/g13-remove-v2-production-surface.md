# V3 G13：删除 V2 生产面

> 完成日期：2026-08-22
>
> 状态：已完成；本记录是开发期破坏式收口与非发布证据，不是发布批准。
>
> 前置基线：[G12 BiliDownloader 验收](./g12-bili-downloader-v3-acceptance.md)

## 1. 结论

Host、Core/UI SDK、四个业务插件、当前测试夹具、统一构建入口和独立测试 ZIP 现在只表达最终 V3
运行语义。无修订保存、可空组合激活、Host 通用事件总线、owner 式全屏、Host 伪插件分支、旧 Dock
Locator 和阶段性 Document 测试双均已从活动面删除，并由源码、反射、真实 NuGet 反向消费和包闭包共同防回流。

G13 没有改变 public API 形状：Core/UI v3 Shipped 继续为空，Unshipped 保持 127/45 条，等待 G14
正式签署。manifest、Document envelope、layout 仍为 schema 2，文件名仍为 `layout-v2.json`，默认数据根
仍为 `v2`。BiliDownloader 等插件自己的业务格式和旧数据迁移也不属于 Host V2 生产面删除范围。

## 2. 设计思路与 SOLID 取舍

| 原则 | G13 落点 |
| --- | --- |
| SRP | 生产协作者继续负责原有业务；G13 脚本只编排零残留、API、包、测试和文档证据。 |
| OCP | 插件仍通过既有 Descriptor 和 V3 SDK 扩展；Host 不新增版本判断或插件类型分支。 |
| LSP | V3 Document、Lifecycle 和全屏实现只接受最终接口前后置条件，旧消费者必须编译失败。 |
| ISP | Core 保持 BCL-only，UI 不引用 Dock/Host；没有为 G13 新增测试专用生产接口。 |
| DIP | 四插件继续只依赖 Core/UI SDK 和自身窄端口，不依赖 Host 实现、Dock 或根 IServiceProvider。 |

实现保持朴素：版本相关测试类改为版本无关名称，活动注释和包描述改为当前 V3 事实；删除证明使用
明确白名单、反射断言和临时 NuGet 消费项目。没有新建 Facade、Manager、通用兼容框架、服务定位器、
反射扫描框架或 V2/V3 双 Loader。

## 3. 删除与保留边界

| 类别 | G13 结果 |
| --- | --- |
| V2 SDK public 入口 | 旧保存、激活、事件总线和全屏 owner 成员不存在；真实 nupkg 反向编译失败 |
| Host internal 入口 | 旧 Factory/Data 投影、Host EventBus、伪插件所有者和 Locator 分支不存在 |
| 当前测试夹具 | Document 保存/关闭/组合上下文使用版本无关名称，不再伪装成 V2 生产协议 |
| 当前构建与包 | 只接受 V3 `IPluginModule` 与 SDK `[3.0.0,4.0.0)`，没有条件编译或 fallback |
| 当前磁盘格式 | manifest/envelope/layout schema 2、`layout-v2.json` 和数据根 `v2` 原样保留 |
| 插件业务兼容 | BiliDownloader、DaTang、MySmallTools 自有旧数据兼容不受 Host SDK 删除影响 |
| 历史证据 | v1/v2 API 文本、计划、验收记录和历史发布脚本保留，但不参与当前 G13 门禁 |

## 4. 失败矩阵

| 失败点 | 门禁行为 |
| --- | --- |
| 活动源码恢复旧 API、Facade、Host 总线、伪插件分支或条件编译 | 精确活动根扫描立即失败 |
| 当前测试或文档恢复阶段性 Document 测试双 | 当前面扫描失败；历史文档不被粗暴改写 |
| SDK DLL 恢复旧类型或方法 | 反射单元测试和 public API 文本同时失败 |
| 打包时留下旧 API 转发 | 十四个真实 NuGet 反向消费者中的对应夹具意外通过，门禁失败 |
| Host/插件闭包出现 Legacy/Common 或 2.x SDK | 本次构建 `.deps.json` 扫描失败 |
| ZIP 携带共享 SDK/Host、两轮内容不一致或不能真实加载 | 包白名单、确定性比较或 Loader/Workspace 测试失败 |
| V2 线格式被误删或改名 | 数据根、信封、布局与版本政策测试失败 |

## 5. 非发布专项门禁证据

统一入口：

```powershell
.\scripts\Test-HostV3ProductionSurface.ps1 -Configuration Release
```

本轮实际结果为零失败、零跳过：

| 套件 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Host Unit | 189 | 0 | 0 |
| Headless UI | 62 | 0 | 0 |
| Plugin / Dock | 204 | 0 | 0 |
| Plugin SDK | 37 | 0 | 0 |
| MyPlugTest | 11 | 0 | 0 |
| DaTangAccountingHelpPlug | 62 | 0 | 0 |
| MySmallTools | 184 | 0 | 0 |
| BiliDownloader | 726 | 0 | 0 |
| 最终 ZIP Loader / Registry / Workspace | 8 | 0 | 0 |
| 合计 | **1483** | **0** | **0** |

Host 行覆盖率为 **84.39%**，分支覆盖率为 **70.58%**，没有降低既有门槛。全解决方案 Release
`TreatWarningsAsErrors` 构建为 0 警告、0 错误；Core/UI v3 API 变异、真实包依赖白名单、两个正向消费者
及十四个反向消费者通过。包协议矩阵的 25 个结构负例也通过。

四插件均从同一源码完成两次隔离构建，文件集合、ZIP 长度和 SHA-256 一致：

| 插件 | 文件数 | 测试 ZIP SHA-256 |
| --- | ---: | --- |
| BiliDownloader | 14 | `0F432D204FA83C1153AB96167CAE23FE1DE9647187CC8C0B4ED0C46F2F34CAD2` |
| DaTangAccountingHelpPlug | 9 | `7728F5B4D9DB8C37EC31A323DCECDF061ED1F9E4D0644B0BDB8AA815C1747635` |
| MyPlugTest | 11 | `508C30DD63DCFD28BDC0CCA9AEFE45A8F8D2C291A12E26363910723C5693CBE6` |
| MySmallTools | 431 | `869C680A5A30A8BD40CEE9A080E99A0516F86FFF750A536A2D850EE94BD433B7` |

机器可读证据位于 `artifacts/test-results/HostV3ProductionSurface/summary.json` 和其
`ManagedPluginPackages/summary.json`。测试数量和摘要是本次审计结果，不硬编码为后续最低门槛。

## 6. 非发布声明与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段没有读取、初始化或修改 AIFLOW，没有运行 Windows CI、Windows Smoke、ReleaseAcceptance、
V1/V2/V3 发布门禁、签名、上传、推送或标签。Release 只表示本地编译配置，所有 ZIP 均为可删除测试制品。

G13 的回滚单位是活动命名清理、边界测试、真实包负例、聚合脚本和当前文档。不得选择性恢复单个旧类型、
测试双、Locator、Host 分支或 fallback；历史 API 文本和 schema 2 数据也不得在回滚中删除或改写。
