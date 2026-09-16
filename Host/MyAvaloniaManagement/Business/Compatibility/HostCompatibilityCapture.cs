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
        var hostPath = AssemblyFilePath.ReadOptional(host);
        if (hostPath is null)
        {
            // 单 EXE 的托管程序集没有 Location。Windows 部署使用正在运行的 EXE 作为完整
            // bundle 身份，其中包含 Host、共享库和自包含运行时；不能改用进程目录里的旧 DLL。
            // 这里不解析 bundle，也不将 EXE 当成托管 PE 读取 MVID。其他平台暂保留未知。
            if (!OperatingSystem.IsWindows()) throw new InvalidDataException("当前平台尚未支持单文件 Host 身份核验。");
            return CreateIdentity(await CaptureBundleAsync(Environment.ProcessPath,
                Assembly.GetEntryAssembly() == host, token));
        }
        var runtimePath = AssemblyFilePath.ReadOptional(typeof(object).Assembly);
        var runtimeDirectory = runtimePath is null ? null : Path.GetDirectoryName(runtimePath);
        var assemblies = new HostContractAssemblyPolicy().SharedAssemblies.Append(host)
            .Distinct().Select(assembly => (Assembly: assembly, Path: AssemblyFilePath.ReadOptional(assembly)))
            .Where(item => item.Path is not null &&
                !string.Equals(Path.GetDirectoryName(item.Path), runtimeDirectory, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var item in assemblies)
            if (PluginArtifactReader.ReadModuleId(item.Path!) != item.Assembly.ManifestModule.ModuleVersionId)
                throw new IOException("磁盘运行时与当前会话不同，请重启 Host 后检查。");
        var files = assemblies.Select(item => (Path.GetFileName(item.Path!), item.Path!)).ToList();
        // 原生绘制依赖也影响 UI 兼容；只加入 Host 输出目录实际提供的文件，BCL 由 Framework 标识补充。
        var directory = Path.GetDirectoryName(hostPath)!;
        foreach (var name in new[] { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" })
            if (File.Exists(Path.Combine(directory, name))) files.Add((name, Path.Combine(directory, name)));
        return CreateIdentity(await ArtifactFingerprint.CaptureFilesAsync(files, token));
    }

    /// <summary>单文件身份必须属于 Host 入口进程；测试运行器或缺失路径不能被误认作 Host。</summary>
    internal static Task<ArtifactIdentity> CaptureBundleAsync(string? executablePath, bool isHostEntryAssembly,
        CancellationToken token = default)
    {
        if (!isHostEntryAssembly || string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            throw new InvalidDataException("无法确认当前单文件 Host 的可执行文件位置。");
        // 本机 Windows 正在运行的映像由系统保护；沿用指纹组件的读取期间变化检查。
        // 外置原生资产仍纳入身份；已打入 bundle 的原生库由 EXE 摘要覆盖，不扫描临时解包缓存。
        var files = new System.Collections.Generic.List<(string Name, string Path)>
            { (Path.GetFileName(executablePath), executablePath) };
        foreach (var name in new[] { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" })
        {
            var path = Path.Combine(Path.GetDirectoryName(executablePath)!, name);
            if (File.Exists(path)) files.Add((name, path));
        }
        return ArtifactFingerprint.CaptureFilesAsync(files, token);
    }

    private static HostCompatibilityIdentity CreateIdentity(ArtifactIdentity identity)
    {
        var host = typeof(HostCompatibilityCapture).Assembly;
        return new HostCompatibilityIdentity(host.GetName().Version!.ToString(3),
            host.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "未知",
            identity.Sha256, RuntimeProfile.Current.Hash, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription,
            PluginSdkCompatibilityProfile.Current.SdkVersion.ToString(3),
            typeof(Avalonia.Application).Assembly.GetName().Version!.ToString(),
            typeof(Dock.Model.Core.IDock).Assembly.GetName().Version!.ToString(), identity.Files);
    }
}
