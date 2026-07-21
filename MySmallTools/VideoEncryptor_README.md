# 视频加密器文档迁移说明

本文件保留用于兼容已有链接。原文档描述的 AES-CTR、完整内存处理和 .NET 8 方案已经过时，不代表当前实现。

当前 MySmallTools 安全视频子系统使用：

- SECVID03 容器；
- PBKDF2-SHA256 与 AES-256-GCM 认证分块；
- 按需解密、可随机定位的播放流；
- .NET 9、LibVLCSharp 3.9.4 和 Windows x64 私有 LibVLC 运行时；
- 每个 Dock Document 独立的依赖注入 Scope，以及原生视频表面重建恢复。

请从新的权威文档入口开始阅读：

- [MySmallTools 安全视频子系统文档](docs/secret-video-player/README.md)
- [概要设计](docs/secret-video-player/architecture-design.md)
- [SECVID03 文件格式](docs/secret-video-player/secvid03-format.md)
- [LibVLC 接入、开发约定与故障排查](docs/secret-video-player/integration-and-conventions.md)

SECVID02 不受当前播放器支持，需要使用当前视频文件加密器重新生成 SECVID03 文件。
