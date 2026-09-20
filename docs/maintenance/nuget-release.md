# 六个 NuGet 包与模板的发布维护

> 用途：SDK/Build/Templates 维护者的发布流程。状态：当前；核对日期：2026-09-16。事实源：[版本属性](../../Directory.Version.props)、各打包项目、[六包发布记录](../archive/records/host-v9/nuget-unified-3.4.1-release.md)。插件作者请先读[快速开始](../quick-start/README.md)。

## 版本与职责

六包职责及当前基线见[版本与交付边界](../reference/platform-baseline.md)。下一次发布先选择尚未存在的新统一版本，修改 `MyAvaloniaPackageVersion` 及必要消费配置；已公开的 3.4.1 不能用不同字节重新覆盖。

Core/UI、Workflow、Icons、Build 和 Templates 共用包发布号；产品版本、插件业务版本、API 基线和磁盘协议不机械跟随。Workflow 保留既有二进制身份，Icons 保持私有资源属性。许可证已由根 [LICENSE](../../LICENSE)及包项目中的 MIT 元数据表达。

## 发布前验证

1. 查询目标六个包 ID 的目标版本是否存在，确认实际发布范围和源码身份。发现冲突即停止，不能用 skip-duplicate 掩盖。
2. 同步当前说明、包 README、模板精确依赖、最低 SDK、三个生成项目的锁文件及受影响的版本政策测试。
3. 执行主仓 `Gate verify`；影响加载/依赖时补真实旧插件或独立消费者验证，按实际输入记录覆盖范围。
4. 打包五个基础包，核对 nuspec、目标框架、许可证、依赖边界、API 和文件清单，记录源码与 SHA-256。
5. 用隔离缓存与本地 feed 验证候选模板生成项目的 locked restore、Release 构建、测试和插件 ZIP。Standalone 通过不能替代真实 Host 集成。

在版本和输出范围已经核对后，基础包入口为：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
dotnet pack Host/MyAvaloniaManagement.PluginSdk -c Release -o artifacts/nuget
dotnet pack Host/MyAvaloniaManagement.PluginSdk.UI -c Release -o artifacts/nuget
dotnet pack Host/MyAvaloniaManagement.PluginSdk.Workflow -c Release -o artifacts/nuget
dotnet pack Host/MyAvaloniaManagement.Icons -c Release -o artifacts/nuget
dotnet pack Packaging/MyAvaloniaManagement.Plugin.Build -c Release -o artifacts/nuget
```

使用隔离的新输出目录或明确文件清单，避免把旧版本包混进候选；以上命令不上传。公共表面新增仍遵循 [API 维护](../reference/plugin-sdk-api-compatibility.md)，不能为包发布改写旧 API 承诺。

## 基础包、锁文件与模板顺序

1. 冻结验证通过的五个基础 nupkg 和对应 snupkg，上传这一批文件；凭据仅使用短期、最小范围的进程环境或受控 Secret，不进入命令摘要、源码和证据。
2. 等待基础包公共源可读，用 NuGet.org-only 配置和全新缓存重新生成、核对模板三个 lock file。不能仅凭本地 feed 哈希声称公共消费通过。
3. 公共源锁文件就绪后打包最终 Templates：

   ```powershell
   dotnet pack Packaging/MyAvaloniaManagement.Plugin.Templates -c Release -o artifacts/nuget
   ```

4. 用隔离模板环境安装最终本地 nupkg，验证普通名称和点分名称生成、还原、构建、测试和 ZIP；随后上传同一模板文件，不重新构建替换。
5. 从公共源安装目标版本模板，以仅公共源、全新缓存重复 locked restore、Release 零警告构建、测试和 ZIP；再在真实 Host 验证加载、私有依赖和贡献。
6. 下载公开包核对版本、依赖和条目内容。NuGet 仓库签名可能改变包文件 SHA-256，归档哈希与 NuGet contentHash 应分别核对。

任一上传失败停止后续上传并核查已发布状态，不覆盖或重打同版本。完成后处理一次性凭据撤销；记录上传回执、公共消费结果、源码 revision、包摘要及未完成事项，不保存凭据正文。

## 开发依赖发布与 Host 产品发布

NuGet 发布和 Host 安装程序发布独立。现有 verify 不授予 Host 发布资格；当前 API Unshipped 状态也不满足 seal 条件。Host 整体人工验收已由所有者确认；公开发布前仍需处理[集中待办](../roadmap/README.md)中的外部业务、发布条件及当次备份/回退验证。

不把历史 PowerShell 脚本恢复成维护入口；旧范围、版本和签署记录只从[归档](../archive/README.md)查询。下一版本的具体授权与凭据应在实际发布任务中取得，本文本身不执行任何发布。
