# Windows 打包、macOS 运行实验

> 用途：当前使用或开发指南；macOS 页面仍为实验说明。核对日期：2026-09-16。版本与支持范围见[集中基线](../reference/platform-baseline.md)，实现依据见本文对应源码或验收链接。

本实验提供自带 .NET 10 运行时的 Host `.app` ZIP，以及独立 MyPlugTest 插件 ZIP。
目标是验证 Windows 交叉发布和 Mac 复制插件的流程。macOS 最低版本为 14；Apple 芯片使用
`osx-arm64`，Intel 使用 `osx-x64`。此包没有 Developer ID 签名或 Apple 公证，首次使用需要在
Mac 本地执行一次准备脚本。Windows 构建和文件检查不能代替 Mac 真机验收。

## 1. 在 Mac 安装和启动 Host

1. 选择与芯片对应的 `MyAvaloniaManagement-3.0.0-osx-…-experiment.zip`，解压。
2. 把解压出来的**整个文件夹**放到个人应用程序目录 `~/Applications`（没有就新建），
   也可以放到其他自己有写权限的固定目录。保留 `.app` 和 `Controls` 的相对位置。
   不要只把 `.app` 单独拖走；两个架构的测试包也不要合并到同一目录。
3. 打开“终端”，输入 `bash `（末尾一个空格），把该文件夹里的 `Prepare.command`
   拖到终端，再按回车。它会修复执行权限、仅移除这份实验应用的下载隔离属性，
   在本机对原生文件和 `.app` 做临时签名，并启动 Host。
4. 以后直接双击 `MyAvaloniaManagement.app`。运行此自包含包不需要另装 .NET SDK 或运行时。

例如 Apple 芯片的默认放置方式：

```bash
bash "$HOME/Applications/MyAvaloniaManagement-3.0.0-osx-arm64-experiment/Prepare.command"
```

`Prepare.command` 用的是本机临时签名，不需要购买 Apple 开发者证书，不等于发行签名或公证。
如果 `codesign` 提示缺少开发工具，先按系统提示安装 Xcode Command Line Tools，再重试。
准备脚本只处理本次实验 `.app`，不修改系统全局 Gatekeeper 设置。

## 2. 复制 MyPlugTest 插件

**可以实现“新增文件夹，复制进去，重启就能加载”。** Host 首次启动时可以没有插件。

1. 退出 Host。
2. 解压同架构的 `MyPlugTest-3.0.0-osx-…-experiment.zip`。
3. 把其中 `Controls/MyPlugTest` **整个目录**复制到 Host 同级的 `Controls` 下。
4. 重新打开 Host，在插件状态窗口确认 MyPlugTest 已加载，在功能中心查看测试插件入口。

最终目录示例：

```text
MyAvaloniaManagement-3.0.0-osx-arm64-experiment/
├── MyAvaloniaManagement.app
├── Prepare.command
├── Debug.command
└── Controls/
    └── MyPlugTest/
        ├── plugin.manifest.json
        ├── MyPlugTest.dll
        ├── MyPlugTest.deps.json
        ├── MyAvaloniaManagement.Icons.dll
        ├── EPPlus.dll
        ├── Flurl.Http.dll
        └── …其他随包提供的私有依赖
```

不要套成 `Controls/Controls/MyPlugTest`，也不要只复制入口 DLL。
插件目录名可以自行选择，但目录内必须保留清单、入口 DLL、同名 `.deps.json` 和完整依赖。
同一个插件 ID 只放一份。更新时退出 Host 后整体替换对应插件目录；当前不支持热加载或热卸载。

Windows 仍从可执行文件同级的 `Controls` 加载；macOS 标准 `.app/Contents/MacOS` 布局从
`.app` 同级的 `Controls` 加载。直接运行未包装成 `.app` 的发布目录时，仍从可执行文件同级加载。
绝对插件根路径继续按调用方的指定位置使用。

### Windows 插件能直接复制到 Mac 吗？

- 纯托管、AnyCPU 且没有 Windows 专用 API 的 DLL 可以跨平台，但完整包还需要核对依赖。
- 本仓正式插件构建默认仍是 `win-x64`。本实验明确按 `osx-arm64` / `osx-x64` 重新构建 MyPlugTest，
  让 `.deps.json` 与私有依赖对应目标平台；优先使用本次生成的 Mac 插件包。
- 使用 VLC、FFmpeg、SQLite 等原生库或外部程序的其他插件，必须提供对应 macOS/CPU 的库和程序，
  还要检查其执行权限、签名及 Windows API。不能把 Windows 的原生 DLL/EXE 直接当 Mac 版本运行。
- Host/Plugin SDK 版本仍需满足插件 manifest 的 SDK 区间；Avalonia/UI 等共享程序集由 Host 提供，
  不应再塞进插件私有目录。

MyPlugTest 的加载、UI、文件选择、Excel 与 HTTP 功能仍需在 Mac 上逐项试用；交叉编译成功不表示
每个功能已通过 macOS 运行验收。外部独立插件尚未纳入本次实验。

## 3. 在 Windows 重新打包

在仓库根目录使用 PowerShell 7、仓库指定的 .NET SDK 和 Python 3：

```powershell
pwsh -NoProfile -File build/Publish-MacOSExperiment.ps1
# 只生成 Apple 芯片版：
pwsh -NoProfile -File build/Publish-MacOSExperiment.ps1 -RuntimeIdentifier osx-arm64
```

输出位于 `artifacts/macos-experiment/`：两个 Host ZIP、两个插件 ZIP，以及各架构的
`osx-…-package-evidence.json`（每个文件和 ZIP 的 SHA-256）。`work-…` 目录保留构建和未压缩包便于检查。
如系统中 Python 命令不同，可传 `-Python C:\path\to\python.exe`。

实验脚本显式覆盖 Host 的 `PlatformTarget`；插件通过 `ManagedPluginExperimentalMacOS=true`
启用实验 RID。构建输出隔离，交叉还原使用 `obj/macos-experiment/` 下的独立锁文件，
不会改写仓库正式 `packages.lock.json`。正式 Windows 包入口与默认平台仍保持原来的行为。

发布禁用了 trimming、Native AOT 和单文件合并，以保留动态插件加载所需的程序集和反射信息。
打包脚本核对主要原生库的 Mach-O 架构、自包含运行时、帮助资源、插件清单、目标 RID、
私有依赖、ZIP 内容哈希，并在 ZIP 中保存 Unix 执行权限。

## 4. 真机验收和排错

建议依次验证：

1. 空 `Controls` 启动 Host，检查窗口、停靠、帮助页、关闭后再启动。
2. 退出，复制 MyPlugTest，启动后确认插件状态无加载失败。
3. 打开测试页、消息发送/接收、Excel 文件选择与导出、HTTP 测试入口。
4. 退出，移走 MyPlugTest，再启动确认 Host 可继续工作；放回后再次加载。

如果双击后闪退，先退出可能仍在运行的 Host，再运行：

```bash
bash "/实际安装目录/Debug.command"
```

终端输出同时保存在安装目录的 `macos-launch.log`。Host 自身诊断仍使用既有数据根策略；
启动失败窗口/插件状态窗口显示的诊断路径和错误码也请保留。Mac 上的 WebView、文件选择器、
字体和图形驱动问题需要结合真机日志判断。

## 官方依据

- [Avalonia macOS 部署：跨系统组装 .app、执行权限、签名](https://docs.avaloniaui.net/docs/deployment/macos)
- [.NET macOS 发布与 JIT entitlement](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
- [.NET 10 支持的 macOS 版本与架构](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
