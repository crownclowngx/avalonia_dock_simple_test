using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MyAvaloniaManagement.Compatibility;

/// <summary>读取随工具或 Host 嵌入的唯一规则源，不读取用户目录里的可变加载策略。</summary>
/// <remarks>同一源文件编译到各消费者内部，避免为有限规则新增公共运行时包。MSBuild 直接导入同一 XML。</remarks>
internal sealed class RuntimeProfile
{
    internal static RuntimeProfile Current { get; } = Load();
    internal string[] SharedRoots { get; }
    internal string Hash { get; }
    private readonly Regex _forbiddenAssembly;
    private readonly Regex _forbiddenReference;

    internal RuntimeProfile(string xml)
    {
        var doc = XDocument.Parse(xml);
        string Property(string name) => doc.Descendants(name).Single().Value;
        if (Property("PluginRuntimeProfileSchema") != "1") throw new InvalidDataException("不支持的运行时规则格式。");
        SharedRoots = doc.Descendants("HostSharedAssemblyRoot").Select(e => e.Attribute("Include")!.Value).ToArray();
        if (SharedRoots.Length == 0 || SharedRoots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != SharedRoots.Length)
            throw new InvalidDataException("共享根不能为空或重复。");
        _forbiddenAssembly = new Regex(Property("PluginForbiddenAssemblyPattern"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        _forbiddenReference = new Regex(Property("PluginForbiddenReferencePattern"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // 忽略行尾和缩进差异；摘要表达规则内容，不依赖源码检出平台。
        var canonical = new XElement(doc.Root!);
        foreach (var comment in canonical.DescendantNodes().OfType<XComment>().ToArray()) comment.Remove();
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString(SaveOptions.DisableFormatting))));
    }

    internal bool IsForbiddenAsset(string path) => _forbiddenAssembly.IsMatch(Path.GetFileName(path.Replace('\\', '/')));
    internal bool IsForbiddenReference(string name) => _forbiddenReference.IsMatch(name);

    private static RuntimeProfile Load()
    {
        using var stream = typeof(RuntimeProfile).Assembly.GetManifestResourceStream("Compatibility.RuntimeProfile")
            ?? throw new InvalidDataException("产物缺少运行时规则。");
        using var reader = new StreamReader(stream);
        return new RuntimeProfile(reader.ReadToEnd());
    }
}
