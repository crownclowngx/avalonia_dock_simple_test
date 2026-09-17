# Avalonia 跨窗口布局队列补丁

Host 专用运行时身份：`avalonia-12.1.2-host-layout.1`。固定上游、SDK、程序集身份及 DLL/PDB 摘要以 [baseline.json](baseline.json) 为准。保留 [MIT 许可](LICENSE.md)。

## 修复边界

`layout-ownership.patch` 只修改上游 `LayoutManager`：消费旧任务之前、用户布局回调之后、递归祖先返回之后及安排重试入队之前，确认控件仍属于当前布局根。已经迁走的任务由旧队列丢弃，新根独立完成布局。`InvalidateMeasure` / `InvalidateArrange` 的错误根检查保持原样。

不增加公开 API，不理解 Dock 或插件，不重建页面，不捕获并吞掉布局异常。正文回收器继续只负责控件单一父级和最终释放。这样使布局调度、正文所有权、构建身份三种职责各自独立。

## 构建与来源

```powershell
pwsh -NoProfile -File tools/Build-AvaloniaLayoutPatch.ps1
# 验证不同路径与独立包目录中的确定性重建：
pwsh -NoProfile -File tools/Build-AvaloniaLayoutPatch.ps1 -ForceRebuild -IsolatedRestore
```

脚本从固定 Git 对象创建独立检出，应用受控补丁，以 .NET SDK 10.0.302 构建 net10.0 Base；使用 `restore/` 的锁文件固定六个构建项目的依赖。`runtime-build.targets` 仅排除 Base 不使用的上游整仓打包依赖 Build.Tasks，保留全部实际源生成器及分析器。路径映射、版本、版权年份均固定。

独立 `validation/` 工程复用框架回归，使用官方包的编译资产和候选运行时，不引用 Host 或 Dock。七项测试全通过且测试目录 DLL 摘要匹配后，才写入 `artifacts/avalonia-cross-window-layout/runtime` 和收据。缓存以脚本、补丁、基线、锁文件及测试内容共同标识；缺少收据、无测试、失败、跳过或摘要不符均不视为成功。

程序集保持 `Avalonia.Base, Version=12.1.2.0, PublicKeyToken=c8d484a7012f9a8b`，FileVersion 为 `12.1.2.1`，InformationalVersion 为 `12.1.2-host-layout.1`。使用上游公钥进行 **PublicSign**，不具有上游私钥签名。宿主运行在 .NET 10；不据此承诺 .NET Framework 验签兼容。

## 运行时资产选择

官方 `Avalonia 12.1.2` NuGet 包及全局缓存不变，SDK UI 的 `[12.1.2]` 精确依赖不变。补丁不是同版本伪造 NuGet 包，而是具有单独身份的 Host 运行时资产；编译仍采用官方引用程序集。

Host、三个 Host 测试项目及兼容工具显式声明 `MyAvaloniaUseLayoutPatch=true`。根 targets 导入 [资产选择规则](../../build/MyAvaloniaManagement.AvaloniaLayoutPatch.targets)，在正常复制和发布文件列表形成后替换 Base 运行时，在生成单文件 bundle 前再次验证唯一性与摘要。SDK、插件包、Standalone 不自动携带此补丁。新增引用 Host 的可执行项目也必须声明该属性，并纳入输出身份检查。

维护者诊断时可显式传 `-p:MyAvaloniaUseLayoutPatch=false` 运行官方基线；这不是可交付配置，默认 verify 的构建后摘要检查会拒绝官方旧 DLL。不要修改全局 NuGet 缓存，也不要在单 EXE 生成后旁放一个 DLL 冒充修复。

每次更新补丁都要换独立身份，重新执行独立还原重建、公开 API 比对、Host 回归与普通/单文件产物验证。详细覆盖与桌面待办见[专用维护指南](../../docs/maintenance/dock-cross-window-layout-verification.md)。
