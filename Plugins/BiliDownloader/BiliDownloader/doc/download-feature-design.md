# BiliDownloader 视频下载功能设计文档

## 1. 功能概述

BiliDownloader 是一个基于 Avalonia 插件化架构的 Bilibili 视频下载工具，支持：

- 通过 BV/av 号解析视频信息
- 支持单视频与多P/合集/番剧列表的统一下载
- 用户可选清晰度（360P ~ 8K）
- DASH 格式音视频分离下载 + ffmpeg 合并输出 MP4
- 每个 Document 独立输出目录
- 任务全量持久化到 SQLite，程序重启自动恢复
- HTTP Range 断点续传，大文件中断后从断点继续

## 2. 架构设计

### 2.1 Document + Tool 双模块职责分离

本项目采用 Avalonia Dock 插件化架构，下载功能严格遵循 Document/Tool 职责分离：

| 模块 | 角色 | 职责 |
|------|------|------|
| **BiliDownloaderViewModel (Document)** | 用户交互层 | URL输入、视频解析、清晰度选择、输出目录设置、任务提交 |
| **BiliSchedulerToolViewModel (Tool)** | 后台执行层 | 接收任务、SQLite持久化、执行下载、ffmpeg合并、进度回传 |

两个模块通过 **消息总线 (IMessengerService)** 进行松耦合通信，不直接引用对方实例。

### 2.2 通信流程

```
Document                              Tool (调度器)
┌─────────────────────┐              ┌──────────────────────────────┐
│ 1. 用户输入URL       │              │                              │
│ 2. 调用B站API解析    │              │                              │
│ 3. 用户选择清晰度     │ ──消息总线─> │ 4. 接收 SubmitDownloadTask   │
│    和输出目录         │  SourceDocId │ 5. 拆分Items写入SQLite       │
│ 4. 点击"提交下载"    │              │ 6. 启动后台下载队列           │
│                     │ <──消息总线─ │ 7. 逐个: DASH流获取           │
│ 5. 按DocumentId过滤  │  TargetDocId │    -> 下载视频 -> 下载音频    │
│    实时更新进度       │              │    -> ffmpeg合并 -> 清理临时 │
│                     │              │ 8. 每步: 写SQLite + 发消息   │
│ 6. 加载时从SQLite    │  (直接读)    │                              │
│    恢复未完成任务状态 │              │                              │
└─────────────────────┘              └──────────────────────────────┘
```

## 3. 关键设计取舍

### 3.1 下载引擎：HttpClient 替代 aria2c

**BiliTools 方案**：使用 aria2c 作为 sidecar 进程，通过 JSON-RPC 控制下载。

**本项目的取舍**：

- **选择 HttpClient**：.NET 原生支持，无需额外打包二进制文件，降低插件部署复杂度
- **放弃 aria2c**：虽然 aria2c 支持多线程下载和更丰富的下载策略，但引入 sidecar 会增加插件体积和部署难度
- **补偿措施**：使用 8KB buffer 的流式读写（ReadStream -> FileStream），边下边存，内存占用可控

### 3.2 DASH 格式优先

**决策**：默认且仅支持 DASH 格式下载。

- Bilibili 当前主流视频均支持 DASH（fnval=4048）
- DASH 格式下视频和音频分离，可独立选择画质和音质
- 通过 ffmpeg `-c copy` 合并，无重编码，速度快且无损
- **暂不支持** FLV/MP4 单流格式（覆盖场景有限，且 B站已逐步淘汰）

### 3.3 视频流选择策略

- **视频流**：优先选择 AVC/H.264 编码（codecid=7），兼容性最广；若用户选择的清晰度无 AVC，则回退到任意编码的最高画质
- **音频流**：固定选择 bandwidth 最高的流（通常为 30280 m4a），确保音质最优
- **暂不支持**：HEVC/H.265、AV1 编码的主动选择（作为后续扩展）

### 3.4 wbi 签名实现

Bilibili API 需要 wbi 签名才能正常返回数据。本项目的实现：

1. 请求 `/x/web-interface/nav` 获取 `wbi_img` 中的 `img_url` 和 `sub_url`
2. 提取文件名（不含扩展名）作为 `imgKey` 和 `subKey`
3. 按固定 `mixinKeyEncTab`（64个索引）混排拼接后的字符串，取前32字符得到 `mixinKey`
4. 请求参数加 `wts`（当前Unix时间戳），按key排序，URL编码拼接
5. `w_rid = MD5(query + mixinKey)` 的十六进制小写

**缓存策略**：mixinKey 缓存30分钟，避免每次请求都获取 nav。

**注意**：与 BiliTools 的区别在于不传 `dm_img_str`/`dm_cover_img_str` 等浏览器指纹参数（C# 客户端无 WebGL 环境），目前测试不影响 API 调用。

### 3.5 消息定向：DocumentId 过滤

**问题**：WeakReferenceMessenger 是广播模式，多个 Document 实例会收到所有消息。

**方案**：

- 每个 `BiliDownloaderViewModel` 构造时生成唯一 `DocumentId`（GUID）
- `SubmitDownloadTaskMessage` 携带 `SourceDocumentId`
- `DownloadTaskProgressMessage` 携带 `TargetDocumentId`
- Document 在消息 handler 中过滤：`if (msg.TargetDocumentId != this.DocumentId) return;`

无需修改公共消息框架，完全在业务层解决。

### 3.6 离线消息恢复：SQLite 作为唯一真相源

**问题**：Document 未打开时，Tool 发送的进度消息会丢失。

**方案**：不做独立消息队列，而是：

- Tool 每次更新进度时 **先写 SQLite，再发消息**
- Document 加载时 **从 SQLite 查询** 自己的未完成任务状态
- `DocumentId` 持久化到 Document 的 SaveData 中（跨重启不丢）

这确保了即使 Document 关闭、程序重启，任务状态也不会丢失。

### 3.7 断点续传

**实现**：

- SQLite 记录每个任务的 `VideoBytesDownloaded` 和 `AudioBytesDownloaded`
- 下载时定期将已下载字节数写入 SQLite
- 重启恢复时，检查临时文件是否存在及其大小
- 使用 HTTP `Range: bytes={已下载字节数}-` 请求头继续下载
- 文件写入模式从 `FileMode.Create` 切换为 `FileMode.Append`

**临时文件管理**：

- 临时目录：由 `IBiliDataPaths.TempDirectory` 决定，任务目录为 `{TempDirectory}/{TaskId}/`
- 包含 `video.tmp` 和 `audio.tmp`
- 合并成功后自动删除临时文件和空目录

### 3.8 重启后的手动恢复

插件级 Coordinator 由宿主生命周期初始化，不再依赖调度器 Tool 是否进入视觉树：

1. 初始化 SQLite 并加载本地任务事实。
2. 将上次退出前处于下载或合并阶段的任务迁移为 `interrupted`。
3. Tool 显示时只读取任务投影，不启动后台处理队列。
4. `pending` 和 `interrupted` 历史任务均不会在应用启动时自动联网。
5. 只有用户明确提交新任务、点击开始或手动重试后，Coordinator 才启动执行队列。

详细生命周期和 Legacy 插件兼容规则见 `G0-BASELINE-TEST-LIFECYCLE.md`。

## 4. 数据模型

### 4.1 核心模型

| 模型 | 文件 | 用途 |
|------|------|------|
| `BiliVideoItem` | Models/BiliVideoItem.cs | 视频项（含进度/状态，绑定到UI列表） |
| `BiliVideoCollection` | Models/BiliVideoCollection.cs | 视频集合（统一单视频/列表） |
| `BiliQualityOption` | Models/BiliQualityOption.cs | 清晰度选项 |
| `BiliDashResult` | Models/BiliDashResult.cs | DASH播放流解析结果 |
| `BiliDashStream` | Models/BiliDashResult.cs | 单条DASH流信息 |
| `DownloadTaskRecord` | Models/DownloadTaskRecord.cs | SQLite任务记录（含断点续传字段） |

### 4.2 SQLite 表结构

```sql
-- 数据库: 由 IBiliDataPaths.DownloadTaskDatabasePath 决定
CREATE TABLE download_tasks (
    task_id             TEXT PRIMARY KEY,     -- 任务唯一ID
    document_id         TEXT NOT NULL,        -- 关联Document实例ID
    series_title        TEXT,                 -- 系列标题
    item_title          TEXT,                 -- 视频标题
    aid                 INTEGER,              -- avid
    bvid                TEXT,                 -- BV号
    cid                 INTEGER,              -- cid
    quality_id          INTEGER DEFAULT 80,   -- 清晰度ID
    output_directory    TEXT,                 -- 输出目录
    progress            REAL DEFAULT 0,       -- 进度 0~100
    status              TEXT DEFAULT 'pending', -- 状态
    error_message       TEXT,                 -- 错误信息
    temp_directory      TEXT,                 -- 临时文件目录
    video_bytes         INTEGER DEFAULT 0,    -- 视频已下载字节数
    audio_bytes         INTEGER DEFAULT 0,    -- 音频已下载字节数
    created_at          TEXT                  -- 创建时间
);
```

**状态流转**：`pending` -> `downloading_video` -> `downloading_audio` -> `merging` -> `done` / `failed`

### 4.3 消息契约

| 消息 | 方向 | 关键字段 |
|------|------|----------|
| `SubmitDownloadTaskMessage` | Document -> Coordinator | SourceDocumentId, Items, QualityId, OutputDirectory, ExtrasConfig |
| `DownloadTaskProgressMessage` | Tool -> Document | TargetDocumentId, TaskId, Progress, Status, ErrorMessage |

任务和消息不保存 Cookie。`BiliDownloadTaskExecutor` 在每次执行开始时通过
`IBiliCredentialProvider` 获取当前登录凭据快照，并将其仅用于本次网络请求。

## 5. 外部依赖

| 依赖 | 用途 | 是否已有 |
|------|------|----------|
| Flurl.Http | B站API请求 | 已有 |
| Microsoft.Data.Sqlite | 任务/进度持久化 | 已有 |
| Newtonsoft.Json | JSON序列化 | 已有 |
| CommunityToolkit.Mvvm | MVVM基础设施 | 已有 |
| **ffmpeg** | 音视频合并 | **需用户自行安装** |

### ffmpeg 要求

- 程序在系统 PATH 中查找 `ffmpeg.exe`
- 若未找到，下载任务会报错提示用户安装
- 推荐从 https://ffmpeg.org/download.html 下载并添加到 PATH
- 后续可扩展为自动下载或内置 ffmpeg

## 6. 当前限制与后续扩展方向

### 当前限制

- 仅支持普通视频（暂不支持番剧付费集、课程等需要特殊权限的内容）
- 不支持 b23.tv 短链自动展开
- 不支持弹幕、字幕、封面等附加资源下载
- ffmpeg 需用户手动安装
- 单线程下载（无 aria2c 多线程加速）

### 后续可扩展

- 番剧/课程付费视频支持（需登录态+大会员验证）
- b23.tv 短链自动跟踪重定向
- 弹幕下载（XML格式）
- 字幕下载（JSON格式）
- 封面/缩略图下载
- 多线程下载（引入 aria2c 或自实现分块下载）
- ffmpeg 自动下载/内置
- 下载速度限制
- 并发下载数控制
