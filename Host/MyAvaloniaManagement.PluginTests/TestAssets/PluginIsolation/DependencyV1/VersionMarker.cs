namespace PluginIsolation.Dependency;

/// <summary>
/// 测试依赖 V1 的稳定探针。
/// 设计意图：与 V2 保持完全相同的程序集简单名称和类型全名，只让程序集版本与返回值不同。
/// </summary>
public static class VersionMarker
{
    public static string Value => "private-v1";
}
