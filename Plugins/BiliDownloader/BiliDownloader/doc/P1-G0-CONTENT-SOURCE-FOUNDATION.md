# P1-G0：统一内容源基础实现记录

> 实施日期：2026-08-05
> 当前结论：代码与离线自动化已实现；P0-G8 正式联网发布门禁尚未完成，因此不在总路线图标记 P1-G0 完成。

## 1. 设计目标

P1-G0 将“用户输入一条链接”改造成可扩展的内容源入口，同时保持 P0 的 UI、Document V2、SQLite、下载执行器和提交行为不变。当前只注册 `DirectLinkProvider`，但 UP 投稿、收藏夹、历史等后续来源可以实现同一契约，无需在 ViewModel 增加来源类型分支。

实现只采用直接解决现有问题的四种模式：

- Strategy：`IContentSourceProvider` 隔离每类来源的规范化、分页和展开语义。
- Registry：`IContentSourceProviderRegistry` 保证一个来源类型只有一个实现。
- Adapter：DirectLink Provider 将统一来源项适配到现有 `BiliVideoCollection/BiliVideoItem`。
- Accumulator/Guard：`ContentPageAccumulator` 统一执行跨页去重和重复游标保护。

没有引入 Provider 基类、反射扫描或事件总线。Provider 通过普通 DI 显式注册，降低后续调试和测试成本。

## 2. 职责边界

统一解析链路固定为：

```text
VideoParseViewModel
  -> IContentSourceProviderRegistry
  -> DirectLinkProvider
       -> IBiliContentSourceApi
       -> IBiliCredentialProvider
  -> IBiliMediaProbe
  -> VideoParseResult
```

- ViewModel 只表达解析意图、展示状态并在完整成功后提交结果。
- Provider 负责链接身份、来源能力、单页协议和普通视频/番剧路由。
- `BiliApiService` 继续负责 HTTP、WBI 和 JSON 映射，并通过两个窄接口分别投影内容目录与媒体质量能力。
- Provider 不引用 ViewModel、Coordinator、SQLite 或 Document。
- 下载执行器仍直接使用原有 API 和任务模型，G0 不改变执行链路。

P0 的旧构造入口被保留，但它们内部同样创建 Registry 与 DirectLinkProvider，不存在绕过统一契约的第二条解析路径。

## 3. 身份与分页不变量

DirectLink 的稳定来源 ID 为：

- `video:bv:{payload}`：仅规范化 `BV` 前缀，payload 保持大小写。
- `video:av:{aid}`。
- `bangumi:ep:{epId}`、`bangumi:ss:{seasonId}`、`bangumi:md:{mediaId}`。

数字 ID 删除前导零并要求为正数。`ContentItemKey` 表达“某来源中的一个项目”；`MediaUnitKey` 只由正数 Aid + Cid 组成，表达跨来源一致的媒体单元。两者不能互换，也不使用随机任务 ID 参与相等判断。

`ContentPageRequest` 的 PageSize 范围为 1～100，默认 20。ContinuationToken 与 SnapshotToken 都是 Provider 私有、不透明字符串：调用方只原样回传，不解析、不清理，也不记录原文。

分页累加器按 `ContentItemKey` 保留首次出现顺序。若 Provider 连续返回相同游标且没有新增键，则抛出 `ProtocolViolation` 并停止分页；非分页 Provider 返回下一页、项目来源类型错误或 HasMore 与游标矛盾也视为协议错误。

## 4. 兼容、取消与安全

- BV、AV、b23.tv、EP、SS 和已有 MD 输入全部经 DirectLinkProvider。
- DirectLink 保持登录前置约束，能力声明为 `RequiresLogin`，能力版本为 1。
- BiliApiService 的集合获取、短链解析、DASH 探测与相关 WBI 请求传播 `CancellationToken`。
- ViewModel 在解析、展开和质量探测全部完成后才原子提交新集合；取消或失败不会覆盖上一次成功结果。
- `ContentSourceDescriptor` 只保存公开参数的防御性副本；DirectLink 不保存任何公开参数。
- `ContentSourceItem` 不提供 Cookie、请求头或签名 URL 字段。
- Provider 不向上层传播远端原始异常文本或 InnerException，避免签名 URL、Cookie 和 token 被 UI 或日志间接记录。

## 5. 离线测试与验收边界

自动化覆盖以下场景：

- 值对象验证、相等性、哈希集合和 Newtonsoft.Json 往返。
- PageSize 边界、游标原样往返、末页状态和筛选值对象。
- Registry 正常查找、未知类型、重复注册、非法能力版本和未知能力位。
- 空页、单页、多页、稳定去重、重复游标和来源类型错误。
- BV/AV/b23.tv/EP/SS/MD 规范化、视频/番剧路由与媒体项适配。
- 登录缺失、非法输入、取消、远端失败、无效 Aid/Cid 和敏感文本隔离。
- ViewModel 取消后保留上次成功结果，以及 DI 中窄接口复用同一 API 单例。

默认测试只使用接口替身、Flurl `HttpTest` 或 loopback HTTP，不访问真实 B 站、不使用真实 Cookie。P0-G8 的真实联网、ffmpeg、Range 和发布包门禁仍是宣告 P1-G0 正式完成的前置条件。

2026-08-05 离线证据：

- BiliDownloader Release 测试 450/450，通过；0 失败、0 跳过。
- 覆盖率门禁：整体行 82.11%、分支 67.53%；A/B/C 风险分组全部达标。
- 全解决方案 Release 构建 0 警告、0 错误。
- 全解决方案测试 837/837，通过；0 失败、0 跳过。

以上证据只证明离线实现与兼容回归通过，不替代 P0-G8 正式联网发布验收。
