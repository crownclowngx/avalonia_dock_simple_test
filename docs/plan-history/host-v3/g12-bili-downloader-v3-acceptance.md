# V3 G12：BiliDownloader 验收

> 完成日期：2026-08-22
>
> 状态：已完成；本记录是开发期非发布证据，不是发布批准。
>
> 前置基线：[G11 MySmallTools 验收](./g11-my-small-tools-v3-acceptance.md)

## 1. 结论

BiliDownloader 最终声明 1 个可持久化 Document、1 个右侧可隐藏 Tool、1 个 Lifecycle 和两个 Creation
Intent。下载、认证、SQLite、FFmpeg、限速、任务恢复、内容来源以及 content schema 3 均未变化。
活动 V2 迁移/包测试命名与脚本已收口为 V3；真实 Host 保存链补齐了捕获后编辑竞争。

插件内部登录、提交、进度、状态、删除和并发消息继续由插件私有消息器承担；SDK/Host 没有恢复通用
EventBus 或 Facade。Tool 在 readiness 未就绪时不访问 SQLite、设置或 FFmpeg，释放后不接受迟到回调。

## 2. 设计思路与职责时序

Document 只拥有下载方案、Revision 与 Dirty；`BiliDownloaderDocumentStateMapper` 只负责严格 schema 3
候选状态；Host 保存服务只负责信封和原子提交。保存竞争与 G10 相同：只确认实际落盘的捕获 Revision，
期间新修改保持 Dirty，第二次保存才清脏。

Lifecycle 的顺序保持：初始化本地状态，加载设置/限速，探测 FFmpeg，恢复登录，再初始化 Coordinator 并
发布 Ready；任一步失败或取消均标记不可用并最佳努力停止已启动组件。停止幂等，只有全新对象图才能从
失败后恢复。业务测试使用隔离数据路径；ZIP 组合测试只验证真实 Lifecycle 可解析，不启动用户数据读写。

私有消息器同步按订阅顺序投递精确消息类型。订阅令牌只移除自身并支持重复释放；处理器异常原样传播并
停止后续处理，发布快照允许已进入队列的处理器最后执行一次，但 Document/Tool 的 Closing/Disposed
门闩会抑制 UI 副作用。

## 3. SOLID 对照

| 原则 | G12 落点 |
| --- | --- |
| SRP | Document 管方案，Mapper 管内容，消息器管插件内通信，Lifecycle 管启动停止，Host 管文件与 Workspace。 |
| OCP | 1 Document、1 Tool、1 Lifecycle 使用既有贡献契约；Host 无 Bili 类型分派。 |
| LSP | New 两种意图、Restore、关闭取消和 Revision 保存均遵守统一 Document 前后置条件。 |
| ISP | Tool 依赖 readiness、仓储、设置和 FFmpeg 窄端口；消息器只暴露 Publish/Subscribe。 |
| DIP | ViewModel 与 Lifecycle 依赖业务接口，不依赖 Host EventBus、Dock、Window 或 IServiceProvider。 |

没有新建通用 Repository、Facade、Manager、事件框架或测试专用生产接口。

## 4. 兼容边界与删除面

- 保持插件 3.0.0、manifest schema 2、SDK `[3.0.0,4.0.0)` 和 content schema 3；
- SQLite、凭据、任务恢复、FFmpeg 与限速 schema/算法不变；
- 活动 G12 V2 Migration/Package 类和脚本已删除，业务历史中的 Document V1/V2/V3 名称仍保留；
- 结构门禁禁止 Legacy、Dock、旧保存/JSON、Host EventBus/Facade、服务定位器和过渡构建面回流；
- 最终 ZIP 只含 win-x64 RID 闭包，真实 `PluginLoadContext` 可解析私有 SQLite 依赖。

## 5. 实际自动化证据

```powershell
.\scripts\Test-BiliDownloaderV3.ps1 -Configuration Release -NoRestore
```

| 套件 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Plugin SDK | 37 | 0 | 0 |
| Host Unit | 188 | 0 | 0 |
| Headless UI | 62 | 0 | 0 |
| Plugin / Dock | 204 | 0 | 0 |
| BiliDownloader | 726 | 0 | 0 |
| 最终 ZIP Loader / Workspace | 2 | 0 | 0 |
| 合计 | **1219** | **0** | **0** |

Host 覆盖率为 **84.39% / 70.58%**。Bili 总体为 **83.80% / 67.54%**；A 组
**89.09% / 76.82%**，B 组 **85.12% / 69.22%**，C 组 **76.80% / 56.55%**，总体容差、
A/B/C 和 17 个关键文件门槛全部通过。

两次隔离构建均生成 14 文件 ZIP，SHA-256 为
`54A396939080E2E93C84B621E4BC86528A9F2BE8993FC42FF8732637C212D8F5`。真实 V3 组合验证 1
Document、2 个 Creation Intent、1 Tool、1 Lifecycle、win-x64 RID 和私有依赖解析。机器证据位于
`artifacts/test-results/BiliDownloaderV3/summary.json`。

## 6. 非发布声明与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段未运行 AIFLOW、Windows CI/Smoke、历史 Bili 发布验收、签名、上传或标签。G12 的回滚单位是
Bili 活动验收测试、Host 保存竞争测试、V3 专项脚本和当前文档；不得修改业务磁盘 schema、恢复 Host
EventBus/Facade 或把测试 ZIP 作为发布候选。
