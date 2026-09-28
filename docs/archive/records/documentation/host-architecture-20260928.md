# Host 分层架构文档建设记录（2026-09-28）

> 状态：文档建设和本次范围内验证已完成。源码基线、文件摘要和验证汇总见[证据 JSON](host-architecture-20260928-evidence.json)。本记录不扩展产品人工验收、部署或公开发布结论。

## 范围与结果

新增 [Host 架构文档组](../../../host-architecture/README.md)，共 65 篇 Markdown、59 张 Mermaid 图，按全景、九个职责模块、八个跨模块场景和查找索引组织。重点解释概念、职责、时序、状态、提交点、失败回滚、线程及资源所有权。

源码索引关联 102 个 Host/SDK C# 文件，正文包含 10 段关键源码片段；省略说明性注释的片段已标明。现有主导航、Host 文档入口、原架构文章和文档分类规则增加新入口。原帮助章节身份和路径保留；没有修改产品 C#、XAML、项目配置、版本、SDK API 或业务行为。

建设顺序为：全景 → 运行时/插件/工作区 → 文档/Dock → 命令/Workflow/呈现/支撑 → 跨模块场景/索引 → 链接、嵌入与渲染验证。具体编写标准见[维护说明](../../../host-architecture/11-reference/05-maintaining-this-guide.md)。

## 验证

| 检查 | 结果 |
| --- | --- |
| 新文档组与四个修改入口的本地文件链接 | 1091 处检查，无失效目标 |
| 从新总览遍历全部新文档 | 65 篇全部可达，无孤立页 |
| C# 片段与当前源码比对 | 10 段通过，忽略空白及已说明省略的注释 |
| Host Debug 构建 | 通过，0 警告、0 错误 |
| HelpContentTests | 20 通过，0 失败、0 跳过 |
| HelpWindowTests | 4 通过，0 失败、0 跳过 |
| 新文档实际嵌入及文本一致性 | 65 篇逐一通过 |
| 生产 HelpMarkdownRenderer 渲染 | 65 篇通过 |
| 帮助内 Markdown 链接解析 | 509 处通过；仓库专用目标单列 |
| 生产 reader.js / reader.css / Mermaid 浏览器检查 | 65 篇 × 浅色/深色，118 次图示渲染，无渲染错误或零尺寸 SVG |
| git diff --cached --check | 通过 |

执行的持久工程检查命令：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj -c Debug --filter FullyQualifiedName~HelpContentTests --logger trx --results-directory artifacts/documentation/host-architecture-20260928/unit -m:1
dotnet test Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj -c Debug --filter FullyQualifiedName~HelpWindowTests --logger trx --results-directory artifacts/documentation/host-architecture-20260928/ui -m:1
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-restore -m:1
git diff --cached --check
```

额外验证使用临时 .NET 反射程序，调用刚构建 Host 中的 HelpContentCatalog 和 HelpMarkdownRenderer，将 65 篇实际嵌入正文导出至本地浏览器夹具。夹具复用生产 reader.js、reader.css 和 Mermaid；本组没有公式，MathJax 仅替代启动等待，不据此声称验证公式。浏览器逐篇等待 rendered 通知、对比原 Markdown 围栏数量、检查错误标记及 SVG 尺寸，并抽查模块总图实际显示。

完整 TRX、资源导出、浏览器报告和临时夹具位于本机 `artifacts/documentation/host-architecture-20260928/`，属于可清理开发产物；长期结果与文件摘要保存在本记录及证据 JSON。

## 核查中澄清的边界

初次资源链接检查发现 SDK、Icons、Packaging 和 patches 的八个 README 目标未嵌入 Host。它们在仓库存在，已明确标注为仓库材料；没有为了通过检查扩张产品资源范围。根 docs、Host 内部 docs 和根 README 的链接仍要求帮助解析通过。

浏览器夹具最初按 HTML 的 language-mermaid 标记估计图数，而 Markdig 已转换为 mermaid 节点，导致预期数量误为零；实际图示无渲染错误。修正为读取原 Markdown 围栏数量后重新验证通过。初始报告保留为本地夹具计数错误记录，不改写为产品失败或删除原始结果。

## 未覆盖范围

没有运行全量 Gate verify、seal、发布 Smoke 或新的原生 Host 人工验收。没有部署安装版、发布 NuGet 或网站。文档列出的业务测试是后续修改风险的导航，不表示本次全部执行。
