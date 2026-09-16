using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Compatibility;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>读取实际 Host 与共享运行时文件。只在后台显式检查时计算摘要，不在 UI 线程或每次激活时扫描。</summary>
internal static class HostCompatibilityCapture
{
    internal static async Task<HostCompatibilityIdentity> CaptureAsync(CancellationToken token = default)
    {
        var host = typeof(HostCompatibilityCapture).Assembly;
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblies = new HostContractAssemblyPolicy().SharedAssemblies.Append(host)
            .Distinct().Where(assembly => !string.IsNullOrEmpty(assembly.Location) &&
                !string.Equals(Path.GetDirectoryName(assembly.Location), runtimeDirectory, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (string.IsNullOrEmpty(host.Location)) throw new InvalidDataException("当前单文件 Host 未提供可核验文件身份。");
        foreach (var assembly in assemblies)
            if (PluginArtifactReader.ReadModuleId(assembly.Location) != assembly.ManifestModule.ModuleVersionId)
                throw new IOException("磁盘运行时与当前会话不同，请重启 Host 后检查。");
        var files = assemblies.Select(assembly => (Path.GetFileName(assembly.Location), assembly.Location)).ToList();
        // 原生绘制依赖也影响 UI 兼容；只加入 Host 输出目录实际提供的文件，BCL 由 Framework 标识补充。
        var directory = Path.GetDirectoryName(host.Location)!;
        foreach (var name in new[] { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" })
            if (File.Exists(Path.Combine(directory, name))) files.Add((name, Path.Combine(directory, name)));
        var identity = await ArtifactFingerprint.CaptureFilesAsync(files, token);
        return new HostCompatibilityIdentity(host.GetName().Version!.ToString(3),
            host.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "未知",
            identity.Sha256, RuntimeProfile.Current.Hash, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription,
            PluginSdkCompatibilityProfile.Current.SdkVersion.ToString(3),
            typeof(Avalonia.Application).Assembly.GetName().Version!.ToString(),
            typeof(Dock.Model.Core.IDock).Assembly.GetName().Version!.ToString(), identity.Files);
    }
}
