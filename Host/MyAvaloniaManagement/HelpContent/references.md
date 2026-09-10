# 概念可以互相连接

## 术语速查

| 术语 | 在本项目中的含义 |
|---|---|
| Host | 拥有窗口、工作区、公共规则及插件运行基础的宿主 |
| Document | 一次独立工作会话，通常有独立模型、View 和 Scope |
| Tool | 跨任务的导航或状态面板，遵循单例和可见性规则 |
| Service | 不依赖页面持续可见的业务能力 |
| Dock | 管理面板停靠、比例和可见性的布局机制 |
| Provider / Scope | 依赖实例的解析边界与生命周期边界 |
| Registry / Catalog | 经过校验的贡献声明和查询目录 |
| Workbench Command | 菜单、快捷键及命令面板共用的用户意图命令 |
| Workflow Action | 面向业务编排的执行能力及运行契约 |
| Revision | 文档内容的修订身份，用于保存后的准确确认 |

## 三条阅读路径

**使用者**：[产品介绍](help:product) → [工作台使用](help:workbench)。

**开发者**：[架构介绍](help:architecture) → [活动与需求分解](help:activity) → [软件工业化](help:production)。

**研究与设计**：[注意力与工作台](help:attention) → [可分叉软件基底](help:forkable) → 每篇文章的完整正文与参考文献。

## 参考资料的身份

帮助中的“文献依据”指原文明确引用的研究；“工程模型”指项目为讨论设计而提出的形式化；“研究假说”指仍需要实验检验的判断。引用文献不等于对本产品效果的直接证明。

理论原文随 Host 离线收录，文献网站由系统浏览器打开。参考正文中的历史阶段记录按其原有日期和版本阅读。

## 公式阅读示例

行内表达式 $a^2+b^2=c^2$ 与 \(E=mc^2\) 会使用统一的数学排版。

\[
\begin{aligned}
H(A)&=-\sum_i p_i\log_2 p_i\\
p_i&=1/n\quad\Longrightarrow\quad H(A)=\log_2 n
\end{aligned}
\]

$$
M=\begin{pmatrix}1&0\\0&1\end{pmatrix},\qquad
f(p)=\begin{cases}-p\log_2p,&p>0\\0,&p=0\end{cases}
$$

公式旁的工具可放大并复制 LaTeX；代码中的 `$a$` 或 `\(a\)` 保持为代码。
