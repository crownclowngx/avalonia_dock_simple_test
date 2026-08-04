# BiliDownloader G8 本地验收证据

> 执行时间：2026-08-04
>
> 结论：离线候选通过，正式联网门禁未执行，`publishable: false`

本目录只保留脱敏、可审计的 G8 证据，与其他插件的测试结果隔离。对应实现和判定规则见 [G8 验收与发布文档](../../../Plugins/BiliDownloader/BiliDownloader/doc/G8-P0-ACCEPTANCE-RELEASE.md)。

本次结果：BiliDownloader 396/396、全解决方案 783/783，均为 0 失败、0 跳过；Release 构建 0 警告、0 错误；宿主候选加载、ZIP 复验和敏感扫描通过。真实 Bilibili、ffmpeg 下载/探测及 20 次 Range 恢复因未提供测试 BVID 和临时 Cookie 而显式跳过。

JSON 文件说明：

- `*.acceptance.json`：总门禁、测试计数、候选摘要和发布资格。
- `*.manifest.json`：候选包中每个 payload 文件的长度与 SHA-256。
- `g8-live.json`：真实门禁状态；本次为 skipped。
- `g8-package.json`：独立解压后的封闭文件集与摘要复验。
- `g8-sensitive-scan.json`：脱敏扫描的文件数和问题列表。

本目录中的提交号和 ZIP 摘要属于 dirty 本地候选，不得作为正式发布凭证。正式执行必须在 clean worktree 上运行且不使用 `-AllowDirty`、`-SkipLiveAcceptance`。
