# 跨窗口布局崩溃修复记录

日期：2026-09-17。任务依据为[修复方案](../../../roadmap/host-document-cross-window-layout-crash-fix-plan.md)；维护入口为[专项指南](../../../maintenance/dock-cross-window-layout-verification.md)。

实现采用路线 B：固定 Avalonia Base 的内部队列归属检查。路线 A 的摘除后刷新在布局重入时不能排空旧队列，已保留失败证据并撤销候选。没有修改业务插件、正文所有权、SDK API、Dock 区域命中或 Layout V3 schema。

新增 20 项自动化覆盖 Host 迁移、真实 Headless 标签输入、最终关闭和晚到模板、框架两种队列、递归回调和反复迁移。原区域整组测试改用真实子控件。官方版本下主窗最后/非最后文档的鼠标迁移均失败；候选在同一夹具中通过。

构建采用具有独立身份的 Host 运行时资产，不替换官方 NuGet，不更改 SDK UI `[12.1.2]`。维护上游源码补丁、构建锁文件、独立验证项目、固定 SDK 和 DLL/PDB 哈希；普通及独立还原重建一致。新增默认 verify 的准备和输出检查，并补缺失 DLL、旧缓存、无测试、跳过、失败和阻断传播的门禁测试。

最终测试数量、Gate runId、源码提交、实际 DLL 与隔离发布产物身份见[非嵌入证据](cross-window-layout-fix-evidence.json)。红灯基线见[失败证据](cross-window-layout-red-evidence.json)。源码修复、自动化验证、桌面验收、安装部署分别记录；本轮没有执行原生桌面矩阵，没有再次部署安装目录，没有使用 AIFLOW、Windows CI 或发布门禁。
