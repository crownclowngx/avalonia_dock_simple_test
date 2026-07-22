# G1：凭据与数据安全

> 实施日期：2026-07-22
>
> 状态：已完成
>
> 平台范围：Windows、Linux（包括国产 Linux 发行版）；不包含 macOS

## 1. 完成目标

G1 解决的是插件内部的明文凭据和敏感数据扩散问题：

- Cookie 不再进入提交消息、任务模型和任务 SQLite。
- 凭据 SQLite 只保存 AES-256-GCM 密文信封。
- 日志、任务错误和附加资源摘要共用一套脱敏规则。
- Windows 与 Linux 使用相同的密码学实现和平台路径抽象。
- 旧版本插件内部状态不迁移，以一次明确的全新存储纪元消除历史文本残留。

这不是操作系统级秘密保险箱。`credential.key` 与 `credentials.db` 位于同一用户数据目录，拥有该目录完整读取权限的人可以解密 Cookie。该取舍是有意的：当前目标是消除偶然查看数据库、日志或任务导出时暴露的明文，并检测密文篡改；不承诺抵御当前用户账号、恶意软件或整目录复制。

## 2. 关键决策

### 2.1 不使用 DPAPI

DPAPI 是 Windows 专属能力，会让 Linux 支持依赖另一套实现和迁移逻辑。项目的平台目标已经包含 Linux，因此 G1 采用 .NET 自带的 `AesGcm`，避免核心凭据格式与操作系统绑定。

### 2.2 不自创密码算法

插件只自行定义一个很小的“密文信封”，不设计新的加密算法：

| 字段 | 含义 |
| --- | --- |
| `version` | 当前格式版本，值为 1 |
| `nonce` | 每次写入随机生成的 12 字节 nonce |
| `ciphertext` | Cookie 名称和值序列化后的 AES-GCM 密文 |
| `tag` | 16 字节认证标签，用于拒绝篡改 |

密钥为 32 个密码学随机字节，对应 AES-256。附加认证数据固定为 `BiliDownloader/Credential/v1`，避免不同用途的密文被误用。

### 2.3 key 可见且与凭据库同目录

`credential.key` 是两行 UTF-8 文本：

```text
BILIKEY1
<32 字节密钥的 Base64>
```

可读格式便于诊断和手工清理。Linux 下尽力设置为当前用户读写（`0600`），但文件权限不是本方案唯一或最终的安全边界。

### 2.4 不迁移历史插件状态

首次启动 G1 版本时，如果不存在 `storage_epoch_v2` 标记，插件会删除自身专属的旧、新数据目录，然后创建全新状态。清理范围包括：

- 下载任务库；
- 插件设置；
- 旧明文 Cookie 与新凭据库；
- 插件日志；
- 插件临时文件。

不会删除：

- 用户选择的外部下载目录及已完成媒体文件；
- 宿主管理的 Document 保存文件；
- 其他插件或应用的数据目录。

清理代码只接受目录名严格为 `BiliDownloader` 的完整路径，并拒绝文件系统根目录，避免路径计算错误扩大删除范围。标记写入前失败时，下次启动会继续清理，避免新旧状态混用。

## 3. 代码边界

| 边界 | 职责 |
| --- | --- |
| `IBiliDataPaths` / `BiliDataPaths` | 唯一决定数据、日志、缓存、数据库和 key 的位置 |
| `IBiliLocalStateInitializer` | 执行一次性的 G1 存储纪元切换 |
| `InstallationKeyStore` | 生成、读取、校验和删除可见 key 文件 |
| `ICredentialProtector` | 只负责明文与 AES-GCM 信封的转换 |
| `IBiliCredentialStore` | 只负责凭据 SQLite 的读写与损坏恢复 |
| `IBiliCredentialProvider` | 向执行器提供当前进程内的 Cookie Header |
| `SensitiveDataSanitizer` | 统一处理日志、错误、摘要和待持久化 URL |

这些接口都是围绕实际替换点建立的，没有引入通用仓储、事件溯源或复杂工厂。密码学、文件路径、SQLite 和登录状态各自只有一个变化原因，调用方依赖窄接口，符合 SOLID 中的单一职责、接口隔离和依赖倒置原则。

## 4. 数据与执行流程

登录持久化：

1. 二维码登录返回 Cookie 名称和值。
2. 登录服务先向 Bilibili 验证凭据。
3. `BiliCredentialStore` 序列化完整 Cookie 载荷。
4. `ICredentialProtector` 使用随机 nonce 加密并生成认证标签。
5. SQLite 事务写入单行密文信封。
6. 如果持久化失败，登录仍可作为本次进程的内存会话使用，UI 明确提示“仅本次会话有效”。

历史登录加载：

1. 只有用户明确进入登录流程时才读取并远端验证历史凭据，应用启动不因此联网。
2. 网络不可用时保留密文并允许重试，不把“无法验证”误判为“凭据无效”。
3. Bilibili 明确判定无效时删除持久化凭据。
4. 密文、tag、key 或载荷损坏时删除不可读状态，重新登录后生成新 key。

任务执行：

1. 提交消息和 `DownloadTaskRecord` 不包含 Cookie。
2. 执行器开始一个任务时取得当前凭据的一份内存快照。
3. 主媒体和 Extras 在该次执行中使用同一快照。
4. Cookie 不回写任务模型、任务库、错误或日志。

## 5. 跨平台路径

| 平台 | 数据 | 日志 | 临时文件 |
| --- | --- | --- | --- |
| Windows | `%LocalAppData%/BiliDownloader` | 数据目录下 `logs` | 数据目录下 `temp` |
| Linux | `$XDG_DATA_HOME/BiliDownloader`，未设置时 `~/.local/share/BiliDownloader` | `$XDG_STATE_HOME/BiliDownloader/logs` | `$XDG_CACHE_HOME/BiliDownloader/temp` |

SQLite 原生库预加载同时识别 Windows/Linux 与 x86、x64、arm、arm64 RID。G1 只保证数据安全层不再依赖 Windows；ffmpeg 安装、文件管理器打开方式和 Linux 发行包仍属于后续平台适配工作。

## 6. 统一脱敏

`SensitiveDataSanitizer` 处理以下数据：

- `Cookie`、`Set-Cookie`、`Authorization` 完整请求头；
- `SESSDATA`、`bili_jct`、`DedeUserID`、`buvid` 等已知 Cookie；
- `w_rid`、签名、token、access key、CSRF 等查询参数；
- HTTP/HTTPS URL 的查询串与 fragment。

脱敏同时放在两层：上层在生成 UI 错误和执行摘要时立即处理；`DownloadTaskStore` 在写入 `error_message`、`extras_result_summary` 和封面 URL 时再次处理。后者是持久化的最后防线，避免未来新调用方绕过规则。

## 7. 自动化验证

`BiliDownloader.Tests` 当前 14 项测试全部通过，其中 G1 新增或扩展的验证包括：

- 首次存储纪元清空插件状态但保留范围外文件；
- 生成可见的 32 字节随机 key；
- 相同明文两次加密使用不同 nonce；
- AES-GCM 正确往返并拒绝 tag 篡改；
- 凭据 SQLite 文件不包含 Cookie 名称和值明文；
- 新任务表不存在 `cookie` 列；
- 提交消息和任务模型不存在 `Cookie` 属性；
- 持久化错误、Extras 摘要和资源 URL 查询串被脱敏；
- 日志脱敏移除请求头、Cookie、token 和签名 URL 参数。

验证命令：

```powershell
dotnet test BiliDownloader.Tests\BiliDownloader.Tests.csproj --no-restore -p:SkipPluginDeploy=true
```

## 8. 明确限制与后续工作

- key 与密文库共同泄露时，Cookie 可以被解密；如果未来威胁模型提高，应在 `ICredentialProtector` 后增加可选的系统 keyring 实现，而不改变任务或登录业务。
- Cookie 在实际请求期间必然以内存明文存在；G1 不提供进程内防窃取能力。
- G1 不保留旧任务与设置，因此没有历史任务恢复承诺。
- macOS 不在目标平台范围。
- G2 仍需实现无有效登录态时的 `WaitingForLogin` 和单任务控制。
- G3 仍需完成 SQLite schema version、进度串行化和恢复完整性闭环。
