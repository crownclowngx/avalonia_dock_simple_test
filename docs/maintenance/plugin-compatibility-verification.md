# 插件兼容开发验证

> 用途：维护者验证具体产物，生成可由插件看板读取的证据。本页属于开发流程，不是发布流程。
> 规则和报告含义见[兼容契约](../reference/plugin-compatibility.md)，界面见[插件看板](../quick-start/plugin-status.md)。

## 构建和执行

在主仓根目录，先构建同一次 Host、工具和显式外部验收测试：

```powershell
dotnet build tools/MyAvaloniaManagement.Compatibility -c Release -m:1 -warnaserror
dotnet build Host/MyAvaloniaManagement.UiTests -c Release -m:1 -warnaserror -p:HostExternalPluginAcceptance=true
dotnet run --project tools/MyAvaloniaManagement.Compatibility -c Release --no-build -- --input <冻结的Controls目录或插件ZIP> --host Host/MyAvaloniaManagement/bin/Release/net10.0 --output artifacts/compatibility --ui-tests Host/MyAvaloniaManagement.UiTests/bin/Release/net10.0/MyAvaloniaManagement.UiTests.dll --workspace <明确选择的插件ID,另一个ID>
```

`--input` 接受单插件目录、Controls 目录或包含插件目录的 ZIP。`--host` 必须是与工具实际加载文件一致的 Host 输出；工具会同时核对 UI 测试目录。不要在验收运行中重新构建这些目录。

省略 `--ui-tests` 只执行静态检查；省略 `--workspace` 不执行 Workspace。`--timeout` 为每个独立测试进程的秒数，默认 120，范围 1–3600。业务与真机始终是“未执行”。

Host 内嵌仓库 Markdown，因此应先完成文档修改，再按顺序构建工具和 UI 测试。若在两次构建之间修改文档，工具会正确拒绝不同的 Host 文件；需重新统一构建，不能关闭摘要校验。显式外部验收结束后，用普通 `dotnet build Host/MyAvaloniaManagement.UiTests -c Release -m:1 -warnaserror` 恢复默认测试集合，避免后续 `--no-build` 误用外部变体。

工具将输入复制到每次运行独有的目录，每个插件分别启动验收进程，使用隔离的数据根。不会安装到用户 Host，也不会执行生命周期登录、下载或原生播放。逐插件通过不能推导为多个插件同时组合通过。

每个有效插件生成 `compatibility-report.json`，执行层级有独立的 `result.trx`、`process.log`；根目录 `summary.json` 记录实际输入和整体结果。静态检查失败、执行失败、中断、指定插件缺失都会返回非零。零测试、跳过、多于一个测试均不算该插件验收通过。无效输入不能伪造插件身份，记录在整体错误摘要或进程错误中。

## 报告交付

在 Host 的“工具 → 插件看板…”导入单个报告。导入后点击“检查安装产物”，核对文件身份和环境；导入自身不代表报告适用。维护者也可将经过校验的报告置于 Host 输出根的 `CompatibilityReports` 目录，随 Host 分发，只读发现。

报告必须放在插件目录之外。插件目录应只包含部署产物；日志和业务数据使用 Host 数据根。输入目录内的所有文件都进入产物指纹，任何额外文件都会得到另一份产物身份。

报告来源未经过签名认证。看板只读取数据，不执行报告正文、附件或地址；复制和导出使用受控摘要。保留旧报告以便对照，不能用旧产品版本号把过期证据重新变成通过。

## 本地回归

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror
pwsh -NoProfile -File tools/Verify-PluginApi.ps1 -RunSelfTests
```

默认 verify 重新构建不含显式外部验收的 UI 测试，不依赖个人插件安装目录。外部样本和候选模板消费另外执行、另外保留证据，不使用 Windows CI、seal、发布 Smoke 或发布重复性流程。

已发布 API 比较使用固定版本的微软 ApiCompat；首次运行下载已记录摘要的三个 3.4.1 基线包，后续校验本地缓存。API 文本分类和正式发布条件见[API 维护](../reference/plugin-sdk-api-compatibility.md)。

## 失败定位与保留

维护者需要验证新 Build / Templates 时，可使用隔离候选流程：

```powershell
pwsh -NoProfile -File tools/Verify-PluginCandidate.ps1 -CandidateVersion <未占用的预发布版本> -OutputDirectory <新的证据目录>
```

它统一打包六个本地候选，创建独立模板 hive、源码副本与 NuGet 缓存，验证还原、构建、测试、元数据稳定性和真实 ZIP。仅生成副本切换候选依赖，不修改正式集中版本；结果和临时输入保留供检查，不部署或上传。此流程不与主仓构建并发执行；结束后用默认属性执行完整 verify。

| 现象 | 处理 |
| --- | --- |
| Host 文件不匹配 | 重新按顺序构建工具、UI 测试，再使用同次 Host 输出；不关闭摘要检查 |
| 报告适用性未知 | 显式检查安装产物；身份无法取得时保持未知 |
| 安装内容已变化 | 按现有安装流程重启 Host；窗口不会热加载替换文件 |
| TRX 缺失、零测试或中断 | 查对应进程日志、编译开关和超时；补执行后导入新报告 |
| 报告损坏或 ID 冲突 | 保留原件调查；修正输入或生成新报告，不覆盖已有同 ID 内容 |
| 旧样本丢失 | 记录待补输入，不能重编译一份后称为原旧插件 |

V10 有日期的输入、命令和结果放在[开发记录](../archive/records/host-v10/development-acceptance.md)。保留完整冻结 DLL/原生资源、元数据及逐文件摘要；只保留摘要无法再次验证兼容。
