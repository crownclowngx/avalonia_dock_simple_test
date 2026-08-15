using System;
using System.IO;

namespace MyAvaloniaManagement.Business.Storage;

/// <summary>
/// 解析当前宿主代际唯一的数据根目录。
/// </summary>
/// <remarks>
/// <para>
/// 设计意图：布局、外观和诊断只负责各自文件的读写，不应分别决定数据放在哪里。
/// 该 Policy 集中保存路径所有权和 v1 隔离规则，但不创建目录、迁移文件或解释任何
/// 持久化 schema，因此不会把路径选择与具体存储生命周期耦合。
/// </para>
/// <para>
/// 自动化环境变量表示调用方已经选定的完整数据根。对它再次追加 <c>v1</c> 会破坏
/// Windows Smoke 和测试夹具的隔离约定，所以只有未配置覆盖时才追加当前代际目录。
/// </para>
/// </remarks>
internal static class HostDataRootPolicy
{
    internal const string EnvironmentVariableName = "MYAVALONIA_DATA_DIRECTORY";
    internal const string ProductDirectoryName = "MyAvaloniaManagement";
    internal const string CurrentGeneration = "v1";

    /// <summary>
    /// 使用当前进程环境解析生产数据根，不产生任何文件系统副作用。
    /// </summary>
    internal static string ResolveDefault() =>
        Resolve(
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// 根据显式覆盖和 LocalAppData 基础目录计算规范化绝对路径。
    /// </summary>
    /// <param name="configuredDataDirectory">
    /// 调用方提供的完整数据根；<see langword="null"/>、空或空白表示未配置。
    /// </param>
    /// <param name="localApplicationDataDirectory">
    /// 未配置覆盖时使用的 LocalAppData 基础目录。参数显式传入以便无环境副作用测试。
    /// </param>
    internal static string Resolve(
        string? configuredDataDirectory,
        string localApplicationDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataDirectory))
        {
            // 覆盖值是完整根目录，不拼接产品名或代际；这是测试和部署的稳定契约。
            return Path.GetFullPath(configuredDataDirectory);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        return Path.GetFullPath(Path.Combine(
            localApplicationDataDirectory,
            ProductDirectoryName,
            CurrentGeneration));
    }
}
