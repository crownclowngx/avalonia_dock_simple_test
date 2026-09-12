# V8 主程序本地部署记录

> 日期：2026-09-12，Asia/Shanghai。
> 用户在 V8 开发验收后明确授权提交 Git、编译并发布到 `D:\data\avalonia`。
> 状态：编译、隔离启动、备份、部署和文件校验完成；人工任务观察仍待验收。

## 源码与构建

执行前工作区干净，V8 实现提交 `a1c7eb5` 与开发验收提交 `3959e2d` 已包含完整中文 title、desc，没有创建空提交。

本次从 `3959e2dca643cde2e010edce2b12d5f1705048f4` 创建隔离源码快照，使用 .NET SDK `10.0.302` 编译 Host 及其项目依赖。RID restore 仅写入快照，工作区锁文件与源代码保持不变。

```powershell
dotnet restore <快照中的Host.csproj> -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:SkipPluginDeploy=true
dotnet publish <快照中的Host.csproj> -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:SkipPluginDeploy=true -warnaserror -m:1 -o <暂存发布目录>
```

发布格式与既有部署一致：Release、win-x64、自包含、压缩单文件，不裁剪；原生库与 HelpWeb 内容按项目发布规则随附。编译通过，零警告、零错误。产品版本为 `3.0.0`，文件版本 `3.0.0.0`，信息版本附带源码提交 `3959e2d`。

## 部署结果

| 项目 | 实际结果 |
| --- | --- |
| 完成时间 | 2026-09-12 08:57:01 +08:00 |
| 目标程序 | `D:\data\avalonia\MyAvaloniaManagement.exe` |
| 暂存发布目录 | `artifacts/host-deployment-20260912-085325/publish` |
| 覆盖前备份 | `D:\data\avalonia-host-backups\20260912-085325` |
| 主程序文件 | 153 个，备份与部署后逐项 SHA-256 校验通过 |
| 已安装插件 | 11 个目录、514 个文件，部署前后 SHA-256 完全一致 |
| EXE 大小 | 44,892,796 字节 |
| EXE SHA-256 | `1C02DDB5E5A9D9B1C034E0A49FA3D5E6900451E43021721E53EAE99710ECB3B4` |

仅覆盖本次发布清单中的主程序文件，没有清空目标目录或替换 Controls。复制前确认目标中没有运行的程序，验证目标路径与重解析点；发生复制或校验错误时可从备份恢复本次覆盖文件。

## 验证与边界

暂存目录中的实际发布 EXE 已使用独立数据目录执行启动检查，走真实窗口 Opened／Closing 路径，退出码为 `0`。生成 schema 2 布局，2 个内置 Tool 默认隐藏；标准输出和错误日志为空。该检查没有装载目标 Controls 中的业务插件，也没有使用用户数据目录。

目标程序逐文件校验与暂存产物一致。未启动目标目录中的业务实例；真实用户试用和外部插件业务联调仍按 [V8 开发验收记录](./cognitive-ux-convergence-acceptance.md) 保留待验收。

本次是用户授权的本地发布，没有运行 Windows CI、完整 seal、上传包或发布 tag。此前“未部署”的开发验收结论描述的是此次授权之前的状态，本记录追加后续本地部署事实。

证据：[部署结果](../../../artifacts/host-deployment-20260912-085325/deployment-result.json)、[文件清单](../../../artifacts/host-deployment-20260912-085325/deployment-files.json)、[隔离启动结果](../../../artifacts/host-deployment-20260912-085325/startup-result.json)、[构建日志](../../../artifacts/host-deployment-20260912-085325/publish.log)。
