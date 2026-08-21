# MyAvaloniaManagement Plugin SDK UI

本包是 Managed Plugin V2 的真实 UI 契约程序集，提供模块入口、插件私有 DI 注册、不可变
Document/Tool 描述符、Avalonia View 绑定和全屏展示端口。

本包依赖同版本 Core SDK，并把 Avalonia、Fluent、Semi 和 Ursa 限制为 Host 验证的版本。
Dock 与 Newtonsoft 不属于插件 UI 契约，插件不得通过本包取得或携带这些程序集。

当前仓库处于 V2 G2：包可用于独立编译夹具，Host 与四个业务插件仍使用仓库内部 Legacy 编译桥；
后续容器、Registry、Dock Adapter 和插件迁移完成前，不构成完整 V2 运行时或公开发布承诺。
