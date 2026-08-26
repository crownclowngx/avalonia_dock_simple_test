# G3.1 SDK 候选打包与发布

本阶段只允许发布 Core `3.2.0`、UI `3.2.0`、Workflow `1.0.0` 及符号包。Build、Templates、Host 产品与
业务插件不在发布范围内。

先从干净候选输出生成三个 nupkg/snupkg，并在隔离 feed 对 Host 全量 Unit、Headless UI、Plugin、SDK
回归以及外部 Studio locked restore/build/test/package 执行专项门禁。门禁通过后冻结文件 SHA-256；上传
必须使用这一批字节，不能重新构建后替换。

发布前查询 NuGet.org，任一目标版本已存在即停止。聊天、历史、日志中出现过的 Token 视为暴露并禁止
使用；只接受新建的短期、最小包范围 Token，通过隐藏输入或临时进程环境传递，不写入源码、脚本、命令
摘要、制品或机器报告。任一上传失败立即停止后续包，不覆盖或重打同版本；完成后立即撤销 Token。

公开后使用全新 NuGet 缓存、仅 NuGet.org 源重新执行 locked restore、Release 零警告构建、测试与插件
打包。该流程不是 Host 产品 Release Gate，也不运行 Windows CI、Windows Smoke 或 Host Release Acceptance。

## G3.1 发布记录

2026-08-26 已按 Core `3.2.0`、UI `3.2.0`、Workflow `1.0.0` 顺序上传冻结候选主包与符号包；六次上传
均成功。三个公开包可下载后，Workflow Studio 使用其 G3.1 专项入口的 `PublicOnly` 模式和全新缓存
完成 locked restore、零警告 Release 构建、49/49 测试及两次确定性打包。账户所有者仍须
撤销本次发布使用的一次性 API Key；Key 正文不属于发布记录。

SDK 发布后操作者另行扩大范围，发布 Templates `1.2.0`。该包精确锁定 Core/UI `3.2.0` 与 Build
`1.1.2`，候选 SHA-256 为 `4D9357D5F482E1F69BDF0767BD57F827027A1173A815F24653307A13BAA79101`；
纯 NuGet.org 的全新缓存安装、模板生成、locked restore、Release 构建、测试和插件打包均已通过。
上传提示缺少内嵌 license 元数据，后续不可覆盖 `1.2.0`，应在新版本中修复。
