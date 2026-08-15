using System;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace LegacyNoModule.Plugin;

/// <summary>
/// 模拟 G4 之前只有策略、没有 <c>IPluginModule</c> 的二进制插件。
/// </summary>
/// <remarks>
/// 构造函数故意抛出异常。Managed-only 测试要求程序集在模块结构预检阶段被隔离，
/// 因此宿主不应进入该构造函数，也不应把它当作 public 无参 Legacy 策略激活。
/// </remarks>
public sealed class LegacyDocumentStrategy : IDocumentCreationStrategy
{
    public LegacyDocumentStrategy() =>
        throw new InvalidOperationException("Legacy 策略不应被实例化。");

    public Document CreateDocument(DocumentCreationParams parameters) =>
        throw new NotSupportedException();

    public DocumentMetadata GetMetadata() =>
        new(
            new DocumentTypeId("myavalonia.plugin.legacy-no-module.document.sample"),
            "Legacy sample");
}
