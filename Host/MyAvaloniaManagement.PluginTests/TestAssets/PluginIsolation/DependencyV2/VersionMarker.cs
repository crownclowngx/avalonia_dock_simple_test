namespace PluginIsolation.Dependency;

/// <summary>
/// 测试依赖 V2 的稳定探针。
/// 设计意图：真实制造“同名、同类型、不同程序集版本”，防止测试只验证文件名不同的伪隔离。
/// </summary>
public static class VersionMarker
{
    public static string Value => "private-v2";
}
