# 视频文件加密器 (Video Encryptor)

## 功能概述

视频文件加密器是一个集成在 MySmallTools 项目中的新功能，允许用户选择视频文件、输入密码并对文件进行加密处理。

## 主要特性

### 1. 文件选择
- 支持通过文件选择器选择视频文件
- 显示选中文件的基本信息（文件大小、格式等）
- 支持常见视频格式的验证

### 2. 密码管理
- 密码强度验证（至少6位）
- 密码确认输入
- 密码显示/隐藏切换功能

### 3. 加密处理
- 基于 AES-CTR 模式的视频加密
- 实时进度显示
- 加密速度和剩余时间估算
- 错误处理和状态反馈

### 4. 用户界面
- 直观的操作界面
- 实时状态更新
- 进度条显示
- 清晰的使用说明

## 技术架构

### 文件结构
```
MySmallTools/
├── Models/SecretVideoPlayer/
│   ├── EncryptionTask.cs          # 加密任务模型
│   └── RelayCommand.cs            # 命令绑定类
├── ViewModels/SecretVideoPlayer/
│   └── VideoEncryptorViewModel.cs # 视图模型
├── Views/SecretVideoPlayer/
│   ├── VideoEncryptorView.axaml   # 用户界面
│   └── VideoEncryptorView.axaml.cs # 界面代码后置
├── Business/SecretVideoPlayer/
│   └── VideoEncryptorService.cs   # 业务逻辑服务
├── InitPlug/SecretVideoPlayer/
│   └── VideoEncryptorDocumentStrategy.cs # 文档创建策略
└── Constants/
    └── DocumentTypeIdConstant.cs  # 文档类型常量
```

### 核心组件

1. **VideoEncryptorViewModel**: 主要的视图模型，处理用户交互和数据绑定
2. **VideoEncryptorService**: 加密业务逻辑服务，提供进度回调
3. **SmartVideoEncryptor**: 底层加密引擎，支持 AES-CTR 模式
4. **EncryptionTask**: 加密任务数据模型，跟踪加密状态和进度

## 使用方法

1. 在应用程序中打开"视频文件加密器"文档
2. 点击"选择文件"按钮选择要加密的视频文件
3. 设置输出路径（可选，默认在原文件目录）
4. 输入加密密码（至少6位）
5. 确认密码
6. 点击"开始加密"按钮
7. 等待加密完成，查看进度和状态信息

## 安全特性

- 使用 AES-CTR 模式加密，安全性高
- 密码强度验证
- 保留视频文件头部信息，确保兼容性
- 错误处理和异常捕获

## 扩展性

该加密器设计为模块化架构，可以轻松扩展：
- 支持更多文件格式
- 添加不同的加密算法
- 集成云存储功能
- 批量加密处理

## 注意事项

1. 请确保有足够的磁盘空间存储加密后的文件
2. 请妥善保管加密密码，丢失密码将无法恢复文件
3. 加密过程中请勿关闭应用程序
4. 建议在加密重要文件前先进行备份

## 技术依赖

- .NET 8.0
- Avalonia UI 框架
- System.Security.Cryptography (AES加密)
- CommunityToolkit.Mvvm (MVVM模式)