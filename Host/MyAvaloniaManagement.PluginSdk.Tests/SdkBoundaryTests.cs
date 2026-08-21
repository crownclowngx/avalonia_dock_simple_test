using System.Reflection;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>以反射和项目引用扫描保护唯一的 Core/UI V2 生产入口。</summary>
public sealed class SdkBoundaryTests
{
    [Fact]
    public void Core程序集只引用框架程序集且不存在旧公共面()
    {
        var assembly = typeof(PluginId).Assembly;
        Assert.Equal("MyAvaloniaManagement.PluginSdk", assembly.GetName().Name);
        Assert.All(assembly.GetReferencedAssemblies(), reference =>
            Assert.StartsWith("System.", reference.Name, StringComparison.Ordinal));

        var exportedNames = assembly.ExportedTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("IDocumentCreationStrategy", exportedNames);
        Assert.DoesNotContain("IToolCreationStrategy", exportedNames);
        Assert.DoesNotContain("PluginLifecycleManager", exportedNames);
        Assert.DoesNotContain("DocumentContentSnapshot", exportedNames);
        Assert.DoesNotContain(exportedNames, name => name.EndsWith("JsonConverter", StringComparison.Ordinal));
    }

    [Fact]
    public void Ui程序集不引用Dock和Newtonsoft且注册接口没有独立AddView()
    {
        var assembly = typeof(IPluginModule).Assembly;
        Assert.Equal("MyAvaloniaManagement.PluginSdk.UI", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Dock.", StringComparison.Ordinal) == true ||
            reference.Name == "Newtonsoft.Json");

        Assert.Equal(
            ["AddDocument", "AddPersistableDocument", "AddTool", "UseLifecycle"],
            typeof(IPluginRegistration).GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .OrderBy(name => name)
                .ToArray());
    }

    [Fact]
    public void 窗口交互端口只暴露路径选择和剪贴板结果()
    {
        Assert.Equal(
            ["PickOpenFilesAsync", "PickSaveFileAsync", "TrySetClipboardTextAsync"],
            typeof(IPluginWindowInteraction).GetMethods()
                .Select(method => method.Name)
                .OrderBy(name => name)
                .ToArray());
        Assert.Empty(typeof(IPluginWindowInteraction).GetProperties());
        Assert.DoesNotContain(
            typeof(IPluginWindowInteraction).GetMethods().SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)),
            type => type.Name is "Window" or "TopLevel" or "IStorageProvider" or "IClipboard");
    }

    [Fact]
    public void 全屏端口只负责内容所有权迁移和恢复()
    {
        Assert.Equal(
            ["TryPresent", "TryRestore"],
            typeof(IWindowContentFullscreenHost).GetMethods()
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Empty(typeof(IWindowContentFullscreenHost).GetProperties());
        Assert.Empty(typeof(IWindowContentFullscreenHost).GetEvents());
    }

    [Fact]
    public void 注册泛型约束固定模型生命周期和AvaloniaView边界()
    {
        var methods = typeof(IPluginRegistration).GetMethods().ToDictionary(method => method.Name);
        Assert.Contains(typeof(IPluginDocument), methods["AddDocument"].GetGenericArguments()[0].GetGenericParameterConstraints());
        Assert.Contains(typeof(IPersistablePluginDocument), methods["AddPersistableDocument"].GetGenericArguments()[0].GetGenericParameterConstraints());
        Assert.Contains(typeof(Control), methods["AddTool"].GetGenericArguments()[1].GetGenericParameterConstraints());
        Assert.Contains(typeof(IPluginLifecycle), methods["UseLifecycle"].GetGenericArguments()[0].GetGenericParameterConstraints());
    }

    [Fact]
    public void 生命周期接口只有启动和停止且不再暴露顺序()
    {
        Assert.Equal(
            ["InitializeAsync", "ShutdownAsync"],
            typeof(IPluginLifecycle).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray());
        Assert.Empty(typeof(IPluginLifecycle).GetProperties());
    }

    [Fact]
    public void Legacy项目已删除且活动项目没有旧引用()
    {
        var root = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(
            root, "Host", "MyAvaloniaManagement.LegacyPluginContracts",
            "MyAvaloniaManagement.LegacyPluginContracts.csproj")));

        var actual = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("MyAvaloniaManagement.LegacyPluginContracts", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(actual);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MyAvaloniaManagement.sln")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录。");
    }
}
