using System.Reflection;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>以反射和项目引用扫描保护唯一的 Core/UI V3 生产入口。</summary>
public sealed class SdkBoundaryTests
{
    [Fact]
    public void Core程序集只引用框架程序集且不存在旧公共面()
    {
        var assembly = typeof(PluginId).Assembly;
        Assert.Equal("MyAvaloniaManagement.PluginSdk", assembly.GetName().Name);
        Assert.Equal(new Version(3, 4, 1, 0), assembly.GetName().Version);
        Assert.All(assembly.GetReferencedAssemblies(), reference =>
            Assert.StartsWith("System.", reference.Name, StringComparison.Ordinal));

        var exportedNames = assembly.ExportedTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("DocumentActivationContext", exportedNames);
        // SDK 自己守护类型不存在（包括 internal），Host 测试只负责其消费者和自身旧类型。
        Assert.Null(assembly.GetType("MyAvaloniaManagement.PluginSdk.IHostEventBus"));
        Assert.DoesNotContain("IDocumentCreationStrategy", exportedNames);
        Assert.DoesNotContain("IToolCreationStrategy", exportedNames);
        Assert.DoesNotContain("PluginLifecycleManager", exportedNames);
        Assert.DoesNotContain("DocumentContentSnapshot", exportedNames);
        Assert.DoesNotContain(exportedNames, name => name.EndsWith("JsonConverter", StringComparison.Ordinal));
    }

    [Fact]
    public void 可持久化Document只暴露修订快照与有参确认()
    {
        var methods = typeof(IPersistablePluginDocument).GetMethods()
            .Where(method => !method.IsSpecialName)
            .ToDictionary(method => method.Name, StringComparer.Ordinal);

        Assert.Equal(
            ["AcceptChanges", "CaptureSaveSnapshotAsync"],
            methods.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            typeof(DocumentRevision),
            Assert.Single(methods["AcceptChanges"].GetParameters()).ParameterType);
        Assert.Equal(
            typeof(ValueTask<DocumentSaveSnapshot>),
            methods["CaptureSaveSnapshotAsync"].ReturnType);
        Assert.DoesNotContain("CaptureContentAsync", methods.Keys);
    }

    [Fact]
    public void Ui程序集不引用Dock和Newtonsoft且注册接口没有独立AddView()
    {
        var assembly = typeof(IPluginModule).Assembly;
        Assert.Equal("MyAvaloniaManagement.PluginSdk.UI", assembly.GetName().Name);
        Assert.Equal(new Version(3, 4, 1, 0), assembly.GetName().Version);
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
        var method = Assert.Single(typeof(IWindowContentFullscreenHost).GetMethods());

        Assert.Equal("TryPresent", method.Name);
        Assert.Equal(typeof(IDisposable), method.ReturnType);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal("content", parameter.Name);
        Assert.Equal(typeof(Avalonia.Controls.Control), parameter.ParameterType);
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

        Assert.Empty(FindLegacyReferences(root));
    }

    [Fact]
    public void Ui程序集不公开旧消息包装器或消息框架签名()
    {
        var assembly = typeof(IPluginModule).Assembly;
        Assert.DoesNotContain(assembly.ExportedTypes.SelectMany(type => type.GetMembers()).Select(member => member.ToString()),
            signature => signature?.Contains("CommunityToolkit.Mvvm.Messaging", StringComparison.Ordinal) == true);
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.IMessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessageHandler`2"));
    }

    [Fact]
    public void 扫描覆盖新增项目与模板但不进入生成目录或嵌套仓库()
    {
        var root = Path.Combine(Path.GetTempPath(), "MAV-sdk-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var relative in new[] { "NewArea/Deep/New.csproj", "Packaging/Templates/Template.csproj",
                         "artifacts/clone/Old.csproj", "Host/bin/Old.csproj", "Host/obj/Old.csproj",
                         ".cache/Old.csproj", "Neighbor/Old.csproj" })
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "<Project><!-- MyAvaloniaManagement.LegacyPluginContracts --></Project>");
            }
            File.WriteAllText(Path.Combine(root, "Neighbor", ".git"), "gitdir: elsewhere");
            Assert.Equal(["NewArea/Deep/New.csproj", "Packaging/Templates/Template.csproj"], FindLegacyReferences(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string[] FindLegacyReferences(string root) =>
        EnumerateActiveProjects(root)
            .Where(path => File.ReadAllText(path).Contains("MyAvaloniaManagement.LegacyPluginContracts", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 进入子目录前裁剪生成物，避免先遍历 artifacts 中的临时克隆再过滤文件。
    /// 从根目录动态发现项目和模板，不维护易漏更新的项目白名单；嵌套 Git 仓库、隐藏目录与链接不属于当前源码树。
    /// </summary>
    private static IEnumerable<string> EnumerateActiveProjects(string directory)
    {
        foreach (var project in Directory.EnumerateFiles(directory, "*.csproj")) yield return project;
        foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
        {
            if (child.Name.StartsWith('.') || (child.Attributes & FileAttributes.ReparsePoint) != 0 ||
                new[] { "artifacts", "bin", "obj", "TestResults", "node_modules" }.Contains(child.Name, StringComparer.OrdinalIgnoreCase) ||
                Directory.Exists(Path.Combine(child.FullName, ".git")) || File.Exists(Path.Combine(child.FullName, ".git"))) continue;
            foreach (var project in EnumerateActiveProjects(child.FullName)) yield return project;
        }
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
