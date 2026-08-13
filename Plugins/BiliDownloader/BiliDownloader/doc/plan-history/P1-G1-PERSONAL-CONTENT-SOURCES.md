# P1-G1 个人高频内容来源实现记录

## 决策

链接下载与个人来源是同一个 BiliDownloader Document 的两个创建意图：`quick-url` 与
`personal-source`。意图只决定首次显示的来源步骤，不创建第二种 Document，不在 Document
之间传递链接，也不改变现有保存类型 ID。所选内容解析后统一进入下载配置、预检和后台任务链路。

宿主通过可选 `IDocumentCreationIntentProvider` 扩展菜单；旧插件继续按单入口展示，原有
`IDocumentCreationStrategy` 二方法契约保持不变。

## 组件边界

- `ContentSourcePickerViewModel`：来源类型、稳定 ID 输入和“我的收藏夹”发现。
- `ContentSourceBrowserViewModel`：单一来源的分页、去重、显式选择和批量解析。
- `DownloadSourceWorkflowViewModel`：链接/个人来源模式与返回操作，不处理分页和下载配置。
- 四个 Provider：只实现各自的规范化和分页语义，共享列表项解析器。
- 四个窄 API：隔离 HTTP/JSON；Provider 和 ViewModel 不解释远端响应结构。
- `BoundedContentSnapshotStore`：为稍后再看提供 15 分钟、最多 32 份、每份最多 2000 项的内存快照。

## 登录能力矩阵

| 来源 | 未登录 | 登录 |
| --- | --- | --- |
| 直接公开 URL | 可解析和下载；受限内容由远端鉴权结果阻止 | 可用账号权限访问 |
| UP 主投稿 | 可浏览公开投稿 | 同左 |
| 公开收藏夹链接 | 可浏览 | 同左 |
| 我的/私有收藏夹 | 不可发现或访问 | 可浏览 |
| 稍后再看 | 不可访问 | 可浏览 |
| 历史记录 | 不可访问 | 可浏览 |

本地“未登录”不再是解析、预检和执行的全局阻断条件。只有实际的媒体鉴权失败会把任务置为
`WaitingForLogin`；暂停的公开任务在未登录状态下仍可恢复。

## API 与安全

- UP 投稿使用 WBI 签名空间投稿接口；公开参数与当前 Web 请求保持一致。
- 收藏夹、稍后再看、历史记录各使用独立接口投影。
- 描述符只保存 `mid`、`media_id` 或当前账号稳定标识；Cookie、签名 URL、原始游标不进入任务库。
- 历史播放位置不映射到 `ContentSourceItem`，错误消息不拼接远端原文。
- 分页游标版本化且不透明；跨来源、畸形和过长游标均拒绝。

这些接口属于 Bilibili Web 端内部接口，可能随平台变化。实现参考当前 yt-dlp 的空间投稿与收藏夹
处理方式，并用离线 JSON fixture 固定本项目适配契约；发布前仍需使用有权限的测试账号执行一次手工验收。

## 测试

`PersonalContentSourceG1Tests.cs` 覆盖能力矩阵、规范化、公开/私有边界、收藏夹发现、分页游标、
稍后再看快照稳定性、共享解析出口、HTTP fixture 与错误码映射。全解决方案测试同时覆盖宿主菜单
意图兼容、匿名预检/执行和其他插件回归。
