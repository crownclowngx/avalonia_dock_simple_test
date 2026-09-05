using System.Reflection;
using System.Xml.Linq;
using DaTangAccountingHelpPlug.Plugin;
using MyPlugTest.Plugin;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>锁定生产插件只引用 SDK，Host 引用只允许存在于获准 Harness。</summary>
public sealed class PluginHostBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void HostApiBoundary_两个生产插件程序集不引用Host()
    {
        var assemblies = new[]
        {
            typeof(DaTangAccountingHelpPluginModule).Assembly,
            typeof(MyPlugTestPluginModule).Assembly,
        };

        Assert.All(assemblies, assembly => Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                "MyAvaloniaManagement",
                StringComparison.Ordinal)));
    }

    [Fact]
    public void HostApiBoundary_Plugins目录只有获准测试项目引用Host()
    {
        var pluginsRoot = Path.Combine(RepositoryRoot, "Plugins");
        var consumers = Directory.EnumerateFiles(
                pluginsRoot,
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(ReferencesHostProject)
            .Select(path => Path.GetRelativePath(RepositoryRoot, path)
                .Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(consumers);
    }

    private static bool ReferencesHostProject(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        return XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value!, projectDirectory))
            .Any(path => string.Equals(
                path,
                Path.Combine(
                    RepositoryRoot,
                    "Host",
                    "MyAvaloniaManagement",
                    "MyAvaloniaManagement.csproj"),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "MyAvaloniaManagement.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"无法从 {AppContext.BaseDirectory} 定位仓库根目录。");
    }
}
