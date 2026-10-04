# 2026-10-04 网络插件加载失败修复

## 原因与修复

安装版 Host 的类型预检抛出 `FileNotFoundException`，缺少 `Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60`；网络插件因而被隔离。此前普通构建和插件部署成功不能证明真实 Host 依赖完整。

Host 显式引用日志抽象 NuGet 包 `10.0.10`，并登记为精确共享根。程序集版本与包版本分开判断；插件请求的程序集 `10.0.0.0` 由现有共享身份策略解析。没有修改插件包、放宽禁带规则或更改 NuGet 缓存。

生产改动为 `Directory.Packages.props`、Host 项目和 `build/MyAvaloniaManagement.RuntimeProfile.props`；主仓同步锁文件，新增日志依赖回归并扩展依赖边界测试。设计与复用步骤见[专用维护文档](../../../maintenance/plugin-logging-dependency.md)。

## 实际验证

| 项目 | 结果 |
| --- | --- |
| 本地 Debug 定向回归 | 21 通过，0 失败，0 跳过；覆盖日志身份正反例、SDK 边界和插件隔离 |
| 安装版旧 EXE + 原插件 | 重现日志抽象缺失和类型预检失败；进程仍以 0 退出 |
| 修补 EXE + 隔离网络插件目录 | 启动、关闭成功，无插件失败诊断 |
| 修补 EXE + 实际安装目录全部插件 | 启动、关闭成功，无插件失败诊断；firstFrame 992 ms，ready 2538 ms |
| 实际用户数据下的插件看板 | 已发现 14、可用 14、异常 0；网络插件生命周期初始化成功 |
| 安装产物检查 | 已核对磁盘文件与当前会话已加载的托管程序集身份 |
| Document 创建 | 从功能中心正常打开 HTTP 工作台，显示已停止、尚未监听与空请求列表 |
| 保留内容 | Controls 与 HelpWeb 共 261 个文件替换前后 SHA-256 一致 |
| 依赖和文档 | 全方案 locked restore 通过；Host 修改文档链接、插件 Docs 检查和 git diff --check 通过 |

TRX、隔离启动日志和 UI 文本证据保存在本地 `TestResults/HostLoggingFix`。本次只验证加载、生命周期、文件身份与窗口创建，未启动代理或执行真实客户端抓包，也不代表 M3/M4/M5 完整业务验收。

## 本机交付身份

- 目标：`D:\data\avalonia\MyAvaloniaManagement.exe`。
- 源码：已安装版基线 `66f3f75e84f87d51a2fe68017108189a743aca16` 加上述三处生产补丁；没有把当前主仓后续改动一起交付。
- 配置：Debug、win-x64、自包含压缩单文件、包含原生解包、关闭裁剪，构建警告视为错误；Host 3.0.0、SDK 3.4.1，运行时检查为 .NET 10.0.11。
- 原 EXE SHA-256：`A494A70D3681E3D1191DAB81E2C626704D0F159E41FBB74DDA8CB1877C4D4D73`。
- 修复 EXE SHA-256：`3B8F5ECB4F5063C0C6FC6E47D69B34EA57E741A7A92F238C0B6E0616B717EF59`。
- 原版备份：`D:\data\avalonia\.backups\host-logging-20261004\MyAvaloniaManagement.exe`，创建时与原 EXE 摘要一致。回退前先退出 Host，再核对备份摘要。
- 网络插件版本仍为 1.0.0.0，DLL SHA-256：`B834DF5419320F8176EE432249FA59BC8A30D05804D214C41F2A2221C010F440`。

本次修复文档在验证后写入源码，未重新嵌入已核对的 EXE；最新修复说明以仓库文件为准。未使用 AIFLOW、Windows CI 或发布门禁，未公开发布。

本次隔离构建 worktree 已通过应用归档并清理，源码补丁保留可恢复快照；主仓本轮测试构建已执行 `dotnet clean`。保留测试证据与原版 EXE 备份，不声称历史 TestResults 或缓存已全部删除。
