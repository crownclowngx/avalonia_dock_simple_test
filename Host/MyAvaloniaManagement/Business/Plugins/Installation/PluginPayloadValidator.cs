using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Compatibility;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>同一份无执行副作用校验用于预览、提交和启动前复查；不因预览曾通过而信任后续磁盘。</summary>
internal static class PluginPayloadValidator
{
    internal static async Task<(PluginManifest Manifest, ArtifactIdentity Artifact)> ValidateAsync(string root, CancellationToken token)
    {
        if (!PluginManifestReader.TryRead(root, out var manifest, out var code, out var detail))
            throw new PluginInstallException(code!, detail!);
        if (!PluginCompatibilityEvaluator.TryEvaluate(manifest!, PluginSdkCompatibilityProfile.Current, out code, out detail))
            throw new PluginInstallException(code!, detail!);
        if (!PluginDirectoryLayout.TryCreate(root, manifest!, out var layout, out code, out _))
            throw new PluginInstallException(code!, "插件缺少有效入口程序集或同名依赖清单。");
        if (!PluginCompatibilityEvaluator.HasMatchingPluginVersion(manifest!, AssemblyName.GetAssemblyName(layout!.EntryAssemblyPath).Version))
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "入口程序集版本与插件清单不一致。");
        using (var deps = PluginInstallJson.Read(Path.ChangeExtension(layout.EntryAssemblyPath, ".deps.json"), 16 * 1024 * 1024))
        {
            if (!deps.RootElement.TryGetProperty("targets", out _) || !deps.RootElement.TryGetProperty("libraries", out _))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件依赖清单结构无效。");
        }
        var artifact = await ArtifactFingerprint.CaptureDirectoryAsync(root, token).ConfigureAwait(false);
        if (artifact.Files.Count(file => string.Equals(Path.GetFileName(file.Path), "plugin.manifest.json", StringComparison.OrdinalIgnoreCase)) != 1)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件包必须只有根目录的一份运行清单。");
        foreach (var file in artifact.Files)
        {
            token.ThrowIfCancellationRequested();
            var name = PluginInstallPaths.Relative(file.Path);
            if (RuntimeProfile.Current.IsForbiddenAsset(name)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件包携带了宿主共享程序集。");
            var parts = name.Split('/');
            if (parts.Length > 1 && parts[0] is "runtimes" or "native" && parts[1] != "win-x64")
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件包包含非 win-x64 平台的运行资产。");
            if (name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件包包含其他平台的原生文件。");
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = File.OpenRead(PluginInstallPaths.Under(root, name));
            using var pe = new PEReader(stream);
            var headers = pe.PEHeaders;
            var anyCpu = headers.CorHeader is { } cor && !cor.Flags.HasFlag(CorFlags.Requires32Bit);
            if (headers.CoffHeader.Machine != Machine.Amd64 && !(headers.CoffHeader.Machine == Machine.I386 && anyCpu))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件程序集或原生库不支持当前 x64 平台。");
        }
        return (manifest!, artifact);
    }
}
