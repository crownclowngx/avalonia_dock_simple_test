# P1-G2 订阅、番剧与课程来源实现记录

## 范围与状态

本组新增追番、追剧、订阅合集和课程四类来源。追番、追剧及订阅合集中的可用媒体会进入既有
下载配置流程；课程只负责发现、分页、层级浏览、权限展示和稳定身份映射，不读取 PUGV 播放地址，
也不创建下载提交项。

代码与离线自动化测试已经落地。使用合法账号进行真实链路验收之前，路线图不把 P1-G2 标记为
最终完成。此限制不是降级处理，而是防止内部 Web API 的字段变化被离线 fixture 掩盖。

## 设计与组件边界

```text
来源选择 / 面包屑浏览
        ↓
IContentSourceProvider（目录策略）
        ├─ FollowingBangumi / FollowingCinema
        ├─ Collection
        └─ Course（仅目录）
        ↓ 可选能力
IContentSourceResolutionProvider
        ├─ 追番 / 追剧
        └─ 订阅合集
        ↓
既有 VideoParseResult 与下载配置
```

- `IContentSourceProvider` 只承诺规范化和分页目录，遵循接口隔离原则。
- `IContentSourceResolutionProvider` 是可选解析端口。课程不实现该接口，避免用运行期
  `NotSupportedException` 伪造能力，符合里氏替换原则。
- 一个来源对应一个 Provider 策略；ViewModel 只读取能力与结构化状态，不判断具体 Provider 类型。
- 三个 API 窄接口由一个 HTTP 适配器实现并通过 DI 投影，Provider 不读取 JSON，也不依赖 Avalonia、
  SQLite、Document 或任务协调器。
- 番剧子列表使用 15 分钟、有容量上限的内存快照，保证远端顺序变化不会破坏同一次分页。
  Token 带版本、来源和父键，跨父集合、畸形和过长输入均被拒绝。

## 稳定身份

| 来源 | 父级键 | 子级键 | 解析后媒体键 |
| --- | --- | --- | --- |
| 追番 / 追剧 | `season:{seasonId}` | `season:{seasonId}/ep:{epId}` | `Aid + Cid` |
| 订阅合集 | `collection:{mediaId}` | `collection:{mediaId}/aid:{aid}` | `Aid + Cid` |
| 课程 | `course:{seasonId}` | `course:{seasonId}/ep:{epId}` | 仅映射，不提交 |

来源键描述“从哪里看到”，媒体键描述“实际是哪一个视频”。因此同一视频出现在两个合集时来源键不同，
而媒体键相同；API 调整返回顺序不会改变任何键。

## 权限矩阵

权限策略按 DRM、区域限制、失效或身份缺失、未发布、购买要求、明确可用、未知的顺序分类。
缺字段时返回 `Unknown` 并拒绝选择，不能乐观推断。

| 状态 | 展示 | 可选择 | 可解析 |
| --- | --- | --- | --- |
| Available | 可用徽标 | 追番/追剧/合集可以 | 对应 Provider 支持时可以 |
| LoginRequired | 登录提示并保留已加载页面 | 否 | 否 |
| PurchaseRequired | 需要购买 | 否 | 否 |
| RegionRestricted | 区域限制 | 否 | 否 |
| Expired | 已失效 | 否 | 否 |
| NotReleased | 尚未发布 | 否 | 否 |
| DrmProtected | DRM 保护 | 否 | 否 |
| Unknown | 状态未知 | 否 | 否 |

即使课程课时状态为 `Available`，本阶段也不显示复选框和“解析所选内容”按钮，底部明确显示
“仅支持浏览，暂不创建下载任务”。Cookie、价格、订单、购买记录、原始响应、签名 URL 和游标不进入
模型、日志、Document 或 SQLite。

## 界面变化

个人来源选择区现在有八类：UP 主投稿、收藏夹、稍后再看、历史记录、追番、追剧、订阅合集和课程。
课程同时提供课程 ss/ep 输入与“读取我的课程”快捷入口。

浏览区使用面包屑逐层进入容器，容器显示进入箭头和子项数，媒体叶节点显示权限徽标。每层保存独立的
已加载页、分页游标和勾选状态；返回上级不重新联网。列表继续使用 `VirtualizingStackPanel`，没有采用
远端分页难以稳定虚拟化的树控件。原悬浮返回按钮已移入正常导航布局。

## API 风险与参考

适配器使用追番追剧、订阅合集和 PUGV 课程目录的 Web 接口。这些不是稳定的公开 SDK 契约，发布前
必须复核字段和错误码。第三方项目只用于理解分层方式和制作离线 fixture：

- [yt-dlp Bilibili extractor](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/bilibili.py)：番剧与 Cheese 课程分开处理，并检查课时状态。
- [lifegpc/bili JSONParser2](https://github.com/lifegpc/bili/blob/master/JSONParser2.py)：独立读取已购课程目录。
- [bilibili-API-collect 追番追剧](https://github.com/pskdje/bilibili-API-collect/blob/main/docs/user/space.md)、[课程](https://github.com/pskdje/bilibili-API-collect/blob/main/docs/cheese/info.md)、[合集](https://github.com/pskdje/bilibili-API-collect/blob/main/docs/video/collection.md)：字段语义和离线响应样本参考。

## 测试与真实验收

`SubscriptionContentSourceG2Tests.cs` 覆盖模型校验、JSON 往返、能力接口隔离、权限优先级、稳定身份、
跨父级游标、快照顺序、课程 ss/ep 规范化、课程只读边界、API fixture、错误脱敏和面包屑状态恢复。
全量测试继续覆盖 G0/G1、下载链路和插件生命周期兼容。

发布前用合法测试账号手工完成：

1. 分别打开追番和追剧，进入至少一个系列，验证可用与受限分集。
2. 打开至少两个订阅合集，验证同一视频在不同合集中的来源身份。
3. 打开“我的课程”，再分别输入一个 `ss` 和 `ep` 链接。
4. 验证已购、未购、过期和未发布课时的徽标，且课程始终不能提交下载。
5. 在第二页读取前使登录失效，确认已加载列表与选择不会被清空。
