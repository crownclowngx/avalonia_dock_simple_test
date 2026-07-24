# 安全视频子系统概要设计

## 1. 设计目标

安全视频子系统需要同时满足以下目标：

- 大文件加密与播放的内存占用不随视频总大小线性增长。
- 密码错误、固定头篡改和视频块篡改能够被明确拒绝。
- 解码器可以像读取普通文件一样顺序读取或随机 Seek，无需生成完整明文副本。
- 标题和描述可以在输入密码前展示，并可在不重写大型视频主体的情况下修改。
- 多个 Dock 标签页互不共享播放状态、恢复快照、加密任务和原生播放器。
- Dock 销毁并重建原生 HWND 后，播放器恢复原位置和用户可见的播放或暂停状态。
- 插件关闭后及时释放原生对象、文件句柄、派生密钥和明文缓存。

不在当前范围内的能力包括：跨平台原生运行时、旧容器兼容、云端密钥管理、数字签名和公开元数据真实性认证。

## 2. 分层与职责

业务代码按能力而不是按泛化的 Service/Helper 类型分为六个窄职责区域：

```text
Business/SecretVideoPlayer/
├─ Container/   SECVID03 布局、密码学、公开区和认证随机读取
├─ Operations/  加解密共用状态、预检、错误和输出事务
├─ Encryption/  单文件流式加密与任务进度
├─ Decryption/  单文件/批量解密、失败模型和输出路径
├─ Playback/    LibVLC 运行时、播放器、MediaInput 和表面恢复
└─ Library/     文件夹扫描契约和实现
```

依赖方向固定为 `Container ← Encryption/Decryption/Playback/Library`，`Operations ← Encryption/Decryption`。Container 和 Operations 不反向依赖具体用例，ViewModel 和插件 Composition Root 组合这些职责；子域之间不得形成环。

```mermaid
flowchart TB
    subgraph Host["MyAvaloniaManagement 宿主"]
        Menu["插件菜单 / Document Strategy"]
        ScopeManager["DocumentScopeManager"]
        Dock["Dock Document 生命周期"]
    end

    subgraph Plugin["MySmallTools 插件"]
        Module["MySmallToolsPluginModule"]
        PlayerDoc["SecretVideoPlayerViewModel"]
        LibraryDoc["SecretVideoLibraryViewModel"]
        BrowserVM["VideoLibraryBrowserViewModel"]
        Scanner["VideoLibraryScanner"]
        EncryptDoc["VideoEncryptorViewModel"]
        PlayerVM["VideoPlayerControlViewModel"]
        Player["ISecureVideoPlaybackSession<br/>SecureVideoPlayer"]
        Lease["PlaybackMediaLease"]
        EncryptService["VideoEncryptorService"]
        Encryptor["Secvid03Encryptor"]
        Preflight["StoragePreflightProbe"]
        Transaction["OutputFileTransaction"]
        Stream["SeekableEncryptedVideoStream"]
        Input["SeekableStreamMediaInput"]
        Runtime["LibVlcRuntime"]
        Surface["EmbeddedVideoSurface"]
    end

    Menu --> ScopeManager
    Dock --> ScopeManager
    Module -.->|注册服务| ScopeManager
    ScopeManager --> PlayerDoc
    ScopeManager --> LibraryDoc
    ScopeManager --> EncryptDoc
    PlayerDoc --> PlayerVM
    LibraryDoc --> PlayerVM
    LibraryDoc --> BrowserVM
    BrowserVM --> Scanner
    PlayerVM --> Player
    Player --> Lease
    Lease --> Stream
    Lease --> Input
    Player --> Runtime
    PlayerVM <--> Surface
    EncryptDoc --> EncryptService
    EncryptService --> Preflight
    EncryptService --> Encryptor
    Encryptor --> Transaction
```

各层边界如下：

| 层 | 主要类型 | 职责 |
| --- | --- | --- |
| 插件接入 | `MySmallToolsPluginModule`、三个 `DocumentStrategy` | 声明服务生命周期，由宿主为每个 Document 创建 Scope；不在模块加载时创建 View、LibVLC 或任务 |
| 页面协调 | `SecretVideoPlayerViewModel`、`SecretVideoLibraryViewModel`、`VideoEncryptorViewModel` | 命令、输入校验、状态文本、公开信息编辑和任务取消 |
| 视频库浏览 | `VideoLibraryBrowserViewModel`、`Library.VideoLibraryScanner` | 限流异步扫描当前目录，隔离单文件错误，并按文件名、标题和描述筛选 |
| 播放控件 | `VideoPlayerControlViewModel`、`VideoPlayerControl` | 展示播放快照、转发用户命令，并把表面令牌交给会话；不编排 LibVLC 生命周期 |
| 原生输出 | `EmbeddedVideoSurface` | 在原生句柄真正创建后绑定 `MediaPlayer.Hwnd`，销毁前同步发出表面丢失通知 |
| 播放会话 | `ISecureVideoPlaybackSession`、`Playback.SecureVideoPlayer` | 串行化命令、候选提交、媒体/表面代次、错误和 Dock 恢复 |
| 媒体资源 | `Playback.PlaybackMediaLease` | 独占一代 `MediaPlayer`、`Media`、`MediaInput` 和加密流并规定逆序释放 |
| 流适配 | `Playback.SeekableStreamMediaInput` | 把 .NET 可 Seek 流适配为 LibVLC `MediaInput`，串行化回调并首次失败优先 |
| 容器读取 | `Container.SeekableEncryptedVideoStream` | 验证固定头，按需认证和解密目标块，维护四块 LRU 明文缓存 |
| 操作基础设施 | `Operations.StoragePreflightProbe`、`Operations.OutputFileTransaction` | 统一任务/错误契约、目录和空间检查，以及 partial 的不覆盖提交/回滚 |
| 加密 | `Encryption.IVideoEncryptionService`、`Encryption.ISecvid03Encryptor` | 单文件预检、进度与 SECVID03 流式加密；密码只作为调用参数 |
| 解密 | `Decryption.IVideoDecryptionService`、`Decryption.ISecvid03Decryptor` | 候选/批次预检、失败隔离、逐块认证导出和顺序执行 |
| 格式与公开区 | `Container.Secvid03Format`、`Container.Secvid03Cryptography`、`Container.EncryptedVideoContainer` | 集中布局、严格解析、nonce/AAD/认证和公开信息读写 |
| 原生运行时 | `Playback.LibVlcRuntime` | 从插件私有目录惰性、线程安全地完成一次进程级 `Core.Initialize` |

## 3. 加密数据流

```mermaid
flowchart TD
    Start["预检输入、冲突、写权限和空间"] --> Prefix["检测并读取原视频最小前缀"]
    Prefix --> Header["生成 salt、fileId、noncePrefix 和固定头"]
    Header --> Kdf["PBKDF2-SHA256 派生 256 位密钥"]
    Kdf --> HeaderAuth["认证固定头与原视频前缀"]
    HeaderAuth --> Temp["事务创建同目录唯一 .partial-*"]
    Temp --> Public["写固定头、64 KiB 公开区和原视频前缀"]
    Public --> Loop{"还有视频主体数据？"}
    Loop -->|是| Read["精确读取最多 1 MiB"]
    Read --> Gcm["AES-256-GCM 加密并生成 16 字节标签"]
    Gcm --> Write["写密文和标签，报告进度"]
    Write --> Loop
    Loop -->|否| Flush["FlushAsync + 落盘刷新 + 关闭"]
    Flush --> Commit["File.Move overwrite:false"]
    Temp -.->|异常或取消| Rollback
    Prefix -.->|异常或取消| Rollback
    Header -.->|异常或取消| Rollback
    Kdf -.->|异常或取消| Rollback
    HeaderAuth -.->|异常或取消| Rollback
    Public -.->|异常或取消| Rollback
    Loop -.->|异常或取消| Rollback
```

加密器只保留一个明文块和一个密文块缓冲区。输入流允许其他读取者，但输出事务使用独占写入；输入文件在加密过程中被截断时，精确读取会抛出分类错误，不会为不完整块生成标签。预检不替代提交竞争检查，正式目标永远不会被覆盖。

### 3.1 批量解密与明文提交

批量解密被分为四个窄职责：`VideoDecryptorViewModel` 管理队列、预检展示和取消源，`IVideoDecryptionService` 重新检查候选、预检批次并隔离单项失败，`ISecvid03Decryptor` 只处理一个容器，`IOutputFileTransaction` 负责明文 partial 的提交与回滚。输出命名由独立的 `DecryptionOutputPathResolver` 净化公开文件名、使用固定头扩展名并避让磁盘及批次内冲突。

```mermaid
sequenceDiagram
    participant UI as VideoDecryptorViewModel
    participant Batch as VideoDecryptionService
    participant One as Secvid03Decryptor
    participant Crypto as Secvid03Cryptography
    participant Disk as 文件系统

    UI->>Batch: DecryptBatchAsync(候选、输出目录、密码)
    loop 每个有效且未成功的候选
        Batch->>Batch: 分配不覆盖的安全输出名
        Batch->>One: DecryptAsync
        One->>Disk: 读取固定头和原视频前缀
        One->>Crypto: 派生密钥并认证固定头
        Crypto-->>One: AuthenticationContext
        One->>Disk: 创建唯一 partial
        loop 每个加密块
            One->>Crypto: 认证并解密块
            One->>Disk: 写入已认证明文
        end
        One->>Disk: Flush + Move(no overwrite)
        One-->>Batch: 成功或分类错误
        Batch-->>UI: 项目与总字节进度
    end
```

播放器和导出器共享 `Secvid03Cryptography` 的认证上下文及块解密规则，nonce、AAD 和标签语义只有一个实现。认证上下文释放时清零派生密钥；导出器还会清零复用的明文、密文和标签缓冲区。错误密码发生在创建 partial 之前，块篡改、取消或 I/O 失败由单文件事务清理当前 partial，已成功项目不回滚。

## 4. 加载与播放数据流

```mermaid
sequenceDiagram
    actor User as 用户
    participant Page as SecretVideoPlayerViewModel
    participant VM as VideoPlayerControlViewModel
    participant Player as SecureVideoPlayer
    participant Stream as SeekableEncryptedVideoStream
    participant Input as SeekableStreamMediaInput
    participant VLC as LibVLC
    participant Surface as EmbeddedVideoSurface

    User->>Page: 选择 .secvid
    Page->>Page: 读取公开标题/描述（无需密码）
    User->>Page: 输入密码并加载
    Page->>VM: LoadMediaAsync
    VM->>Player: LoadEncryptedVideoAsync
    Player->>Player: 清理旧 Media/Input
    Player->>Stream: Open(path, password)
    Stream->>Stream: 结构检查、PBKDF2、固定头认证
    Player->>Input: 包装可随机读取流
    Player->>VLC: 创建 Media 并 ParseLocal
    VLC-->>Player: 媒体解析结果
    Player-->>VM: 加载成功
    User->>VM: Play
    VM->>VLC: Play
    VLC->>Input: Open / Read / Seek 回调
    Input->>Stream: 串行化读取
    Stream->>Stream: 命中缓存或认证解密目标块
    Stream-->>VLC: 返回原视频字节视图
    VLC->>Surface: 输出到已绑定 HWND
```

“加载成功”不表示视频主体已经完整解密。打开阶段只验证容器结构、密码和不可变头；某个视频块被篡改时，会在解码器实际读取该块时失败。`SeekableStreamMediaInput.LastError` 保存底层认证或读取异常，供 LibVLC 的通用错误事件生成更有意义的提示。

## 5. 公开信息编辑流程

公开标题和描述不属于视频密文认证数据，因此可以原地修改。页面层必须先切换资源状态，再写文件：

```mermaid
flowchart LR
    Edit["用户进入编辑"] --> Cleanup["Stop 并释放 Media / MediaInput"]
    Cleanup --> Build["校验 Rune、UTF-8 字节和控制字符"]
    Build --> Payload["先写公开区负载和零填充"]
    Payload --> Flush1["Flush 到磁盘"]
    Flush1 --> Record["最后写 32 字节记录头和 CRC"]
    Record --> Flush2["再次 Flush"]
    Flush2 --> Reload["重新读取公开信息；媒体保持未加载"]
```

先写负载、后提交记录头的顺序不能颠倒。进程在中途退出时，旧记录头和新负载不匹配会触发 CRC 错误，而不会把部分写入误认为有效公开信息。公开区损坏不阻止用户继续尝试密码验证和播放，因为公开区刻意不参与 GCM AAD。

## 6. DI 与 Document 生命周期

`MySmallToolsPluginModule` 当前注册关系为：

| 生命周期 | 服务 |
| --- | --- |
| Singleton | `LibVlcRuntime` |
| Scoped | `IPlaybackMediaLeaseFactory`、`ISecureVideoPlaybackSession`、`ILibVlcVideoOutputSource`、播放器/媒体库 ViewModel、`IVideoEncryptionService`、`IVideoDecryptionService` 及两个任务 Document |
| Transient | `IStoragePreflightProbe`、`IOutputFileTransactionFactory`、`ISecvid03Encryptor`、`ISecvid03Decryptor`、输出路径解析器 |
| Transient | `Secvid03Encryptor`、`IVideoLibraryScanner` |

单文件播放器、文件夹视频库和加密器策略都通过 `IDocumentScopeFactory.CreateDocument<TDocument>()` 创建文档。宿主的 `DocumentScopeManager` 维护 Document 与 `IServiceScope` 的一一对应关系；只有 Dock 真正确认关闭后才释放 Scope。

```mermaid
sequenceDiagram
    participant Strategy as DocumentStrategy
    participant Manager as DocumentScopeManager
    participant DI as IServiceScope
    participant Doc as Document ViewModel
    participant Resource as 播放器或加密任务资源
    participant Dock as Dock

    Strategy->>Manager: CreateDocument<T>()
    Manager->>DI: CreateScope
    DI->>Resource: 构造 scoped 依赖
    DI->>Doc: 构造 Document
    Manager->>Manager: 登记 Doc 与 Scope
    Manager-->>Strategy: 返回 Document
    Dock->>Manager: 确认 Document 已关闭
    Manager->>DI: Dispose Scope
    DI->>Doc: Dispose（若实现）
    DI->>Resource: 按容器所有权逆序 Dispose
```

由此产生以下所有权约定：

- 插件模块只注册构造关系，不保存 Scope，也不预创建原生对象。
- ViewModel 可以取消自己发起的异步工作、退订事件和使回调失效，但不能重复释放由 DI 注入且同样由 Scope 托管的服务。
- `SecureVideoPlayer` 独占并释放自己的 `LibVLC`、`MediaPlayer`、`Media` 和 `MediaInput`；进程级初始化不等于原生实例无需释放。
- 关闭加密或批量解密文档只发送取消，不在 UI 线程同步等待任务；底层调用在退出路径删除当前临时文件。
- 宿主退出时，`DocumentScopeManager.Dispose` 会逆序释放仍未关闭文档的 Scope。

## 7. 播放状态与异步回调

`VideoPlayerControlViewModel` 是 LibVLC 线程与 Avalonia UI 线程之间的边界：

- LibVLC 的状态、时间、位置、长度和错误事件通过 `Dispatcher.UIThread.Post` 更新绑定属性。
- 每次加载或清理媒体都会推进 `mediaGeneration`。投递回 UI 队列的回调携带当时的代次；代次不匹配或对象已释放时直接丢弃。
- 用户主动播放、暂停、停止、Seek、切换媒体或清理媒体都会取消尚未完成的视频表面自动恢复。
- 表面恢复请求具有唯一 `RequestId` 且只消费一次；快速重复切换时只有最新快照生效。
- 为表面重建执行的内部 `Stop` 与用户主动 `Stop` 分开计数，防止迟到的原生 `Stopped` 事件误删恢复快照。

Dock/原生表面的详细恢复时序及故障定位见[接入、约定与排障](integration-and-conventions.md#4-dock-切换与视频表面恢复)。

## 8. 关键源码

- [MySmallToolsPluginModule.cs](../../Plugin/MySmallToolsPluginModule.cs)
- [SecretVideoPlayerViewModel.cs](../../ViewModels/SecretVideoPlayer/SecretVideoPlayerViewModel.cs)
- [SecretVideoLibraryViewModel.cs](../../ViewModels/SecretVideoPlayer/SecretVideoLibraryViewModel.cs)
- [VideoLibraryScanner.cs](../../Business/SecretVideoPlayer/Library/VideoLibraryScanner.cs)
- [VideoPlayerControlViewModel.cs](../../ViewModels/SecretVideoPlayer/VideoPlayerControlViewModel.cs)
- [SecureVideoPlayer.cs](../../Business/SecretVideoPlayer/Playback/SecureVideoPlayer.cs)
- [VideoEncryptorViewModel.cs](../../ViewModels/SecretVideoPlayer/VideoEncryptorViewModel.cs)
- [VideoDecryptorViewModel.cs](../../ViewModels/SecretVideoPlayer/VideoDecryptorViewModel.cs)
- [Secvid03Decryptor.cs](../../Business/SecretVideoPlayer/Decryption/Secvid03Decryptor.cs)
- [DocumentScopeManager.cs](../../../../../Host/MyAvaloniaManagement/Business/Helpers/DocumentScopeManager.cs)
