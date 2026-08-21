using System.Reflection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Presentation;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 锁定 G11 删除后的最小 Plugin SDK 表面，并明确保护仍有正式语义的契约。
/// </summary>
/// <remarks>
/// G13 文本基线检测任意签名漂移；本组可读断言继续说明 G11 删除和保留的设计意图，
/// 避免未来仅登记签名就无意恢复通用对象包、占位初始化字段或无生产实现的路径策略。
/// </remarks>
public sealed class G11LowValuePublicSurfaceTests
{
    [Fact]
    public void Document创建参数只保留身份标题和明确入口()
    {
        var properties = typeof(DocumentCreationParams)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["CreationIntentId", "DocumentTypeId", "Title"],
            properties);
    }

    [Fact]
    public void 低价值类型和占位成员不会重新进入Sdk()
    {
        var assembly = typeof(IPluginModule).Assembly;

        Assert.Null(assembly.GetType(
            "MyAvaloniaManagementCommon.Save.IDocumentSavePathPolicy"));
        Assert.Null(assembly.GetType(
            "MyAvaloniaManagementCommon.Behaviors.HandledEventsAwareBehavior"));
        Assert.Null(typeof(DocumentCreationParams).GetProperty("InitializationData"));
        Assert.Null(typeof(DocumentCreationParams).GetProperty("AdditionalData"));
    }

    [Fact]
    public void 低价值生命周期编排面已删除_正式文档语义继续保留()
    {
        var assembly = typeof(IPluginModule).Assembly;

        Assert.DoesNotContain(
            assembly.ExportedTypes,
            type => type.Name is
                "PluginLifecycleManager" or
                "PluginLifecycleState" or
                "PluginLifecycleStage" or
                "PluginLifecycleStatus" or
                "PluginLifecycleOptions" or
                "PluginLifecycleRegistration" or
                "PluginLifecyclePlanBuilder" or
                "PluginLifecycleOperationRunner" or
                "IPluginLifecycleDependencies");
        Assert.Contains(typeof(IDocumentCreationIntentProvider), assembly.ExportedTypes);
        Assert.Contains(typeof(IWindowContentFullscreenHost), assembly.ExportedTypes);
        Assert.Equal(
            typeof(CancellationToken),
            typeof(IDocumentLifetime).GetProperty(nameof(IDocumentLifetime.ClosingToken))?.PropertyType);
        Assert.Equal(
            typeof(bool),
            typeof(IDocumentLifetime).GetProperty(nameof(IDocumentLifetime.IsClosing))?.PropertyType);
    }
}
