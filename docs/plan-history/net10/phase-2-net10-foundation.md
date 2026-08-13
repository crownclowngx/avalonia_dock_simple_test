# 阶段 2：.NET 10 稳定基座验收记录

## 结论

阶段 2 的自动化技术门禁已通过，代码基座可以标记为
`upgrade/net10-foundation`。本记录不替代 G10 性能复测或 G11 人工签字；
现有 G4、G10 历史证据仍只代表其原提交和 .NET 9 环境，阶段 0 记录的
G11 正式签字缺失状态也没有被本阶段改写。

## 构建与依赖基座

| 项目 | 结果 |
| --- | --- |
| SDK | 10.0.302，由 `global.json` 固定并只允许最新补丁滚动 |
| 普通项目 TFM | `net10.0` |
| 真实窗口 Harness TFM | `net10.0-windows` |
| C# | 14.0 |
| 项目数量 | 12 |
| 中央直接包版本 | 38 |
| 锁文件 | 12 个，连续两次 locked restore 后 SHA-256 无漂移 |
| Avalonia | 11.3.18 |
| Dock | 全系列保持 11.3.2.2 |
| 高危/严重公告 | 0 |

`Microsoft.Data.Sqlite 10.0.10` 的默认传递图仍允许
`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。BiliDownloader 因此直接引用中央固定的
`SQLitePCLRaw.bundle_e_sqlite3 2.1.12`，让原生 SQLite 一并进入已修复版本，
而不是使用传递依赖自动提升。

## 警告整改与构建稳定性

- DaTang 必填字符串和集合建立非空默认值。
- 允许解析失败的方法使用可空返回类型，`TryGetValue` 输出显式表达可能缺失。
- 三个同步业务方法保留原 `Task` 契约并返回 `Task.CompletedTask`，没有新增接口层。
- UI 日志清理显式等待 Dispatcher；应用、主窗口和剪贴板能力分别判空。
- ViewModel 使用 MVVM Toolkit 生成的日期属性，不直接访问生成器字段。
- 解决方案构建时，测试与 Harness 不再为项目引用注入不同的全局属性，
  从根因消除了同一输出被并行编译两次导致的 `CS2012`；独立测试仍跳过插件部署。

严格构建命令直接通过：

```powershell
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore -warnaserror
```

结果为 0 警告、0 错误。

## 测试与安全

| 测试组 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| MySmallTools | 180 | 0 | 0 |
| 宿主插件测试 | 25 | 0 | 0 |
| BiliDownloader | 21 | 0 | 0 |
| 合计 | 226 | 0 | 0 |

宿主插件测试新增 4 项 DaTang 回归用例，覆盖模型非空不变量、同步完成的全量清理、
缺失映射下的确定性汇总，以及发票日期起止边界。

现有 SECVID03 向量、篡改检测、生命周期和无完整明文临时文件测试包含在
MySmallTools 的 180 项测试中并继续通过。自 net9 基线标签以来，SECVID03、
播放 Surface、Media Lease 和公共宿主接口均无源代码修改。

依赖命令：

```powershell
dotnet list .\MyAvaloniaManagement.sln package --vulnerable --include-transitive
```

对 12 个项目均返回“没有易受攻击的包”。基线中的
`Tmds.DBus.Protocol 0.21.2`、`SQLitePCLRaw.lib.e_sqlite3 2.1.10` 和
`System.Security.Cryptography.Xml 9.0.3` 已不在当前锁定依赖图中。

## 发布脚本与运行时检查

- 五个发布/验收脚本均通过 PowerShell AST 语法解析。
- SDK 门禁验证主版本 10，同时在证据中保留完整 SDK 版本。
- 当前项目与发布脚本中不存在 `net9.0` 或 `.NET 9` 活动配置。
- 宿主 net10 输出包含 BiliDownloader、MyPlugTest 和 MySmallTools 插件目录。
- MySmallTools 私有运行时包含 `LibVLCSharp.dll`、
  `LibVLCSharp.Avalonia.dll`、`libvlc.dll`、`libvlccore.dll` 及完整 x64 插件树，
  共 425 个原生运行时文件。
- 在 Windows x64 会话中进行了 6 秒宿主启动烟雾检查，进程没有提前退出。

旧 G4/G10 测量结果、阶段 0/1 文档以及升级指南中的 net9 字样属于受控历史记录，
未被改写为 net10 正式证据。

## 人工验收边界

本阶段没有伪造新的 G10/G11 结果。Dock 默认布局的视觉状态、用户旧布局文件恢复
以及真实播放交互仍应在可见桌面会话中按 G11 手册人工确认并签字；它们不影响本次
.NET 10 代码基座、锁定依赖图和自动化测试结论。
