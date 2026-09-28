# 跨模块流程：沿一次真实操作读完整个 Host

> 核对日期：2026-09-28。本篇串联当前实现；局部机制、源码和验证入口见文内下钻链接。

[返回文档总览](../README.md)

## 如何使用本目录

每篇固定一个用户场景，说明入口、跨模块交接、成功提交点及失败分支。局部机制通过链接继续放大，避免把完整算法重复维护在多处。

| 场景 | 主链 | 阅读 |
| --- | --- | --- |
| 启动工作台 | Program → 安装准备 → Runtime → 插件 → Shell | [启动](01-startup.md) |
| 新建插件页面 | 功能入口 → 文档门 → Scope → Adapter → Workspace | [新建](02-create-document.md) |
| 打开、保存和关闭 | 文件 → 内容 → 保存快照 → 关闭许可 → 释放 | [文档工作流](03-open-save-close.md) |
| 页面浮动和布局恢复 | 窗口 → Dock → Session → Snapshot → Store | [布局](04-floating-and-layout.md) |
| 命令面板执行页面命令 | 候选 → 目标约束 → Executor → Target | [命令](05-command-palette.md) |
| 跨插件调用 | Gateway → Run → 授权 → Provider Scope | [Workflow](06-workflow-call.md) |
| 安装并重启 | 预览 → 待办 → 进程交接 → 应用 → 确认 | [安装](07-install-and-restart.md) |
| 退出工作台 | 关闭许可 → 最终布局 → 排空 → 容器释放 | [退出](08-shutdown.md) |

## 图的阅读约定

时序中的箭头表示逻辑调用或交接，未逐一展开 Dispatcher、await continuation 或底层框架回调。需要判断线程或资源寿命时，结合[所有权与线程矩阵](../11-reference/02-ownership-and-threading.md)。标注成功条件的步骤才是该场景的提交点，不能把“请求已发出”当作完成。
