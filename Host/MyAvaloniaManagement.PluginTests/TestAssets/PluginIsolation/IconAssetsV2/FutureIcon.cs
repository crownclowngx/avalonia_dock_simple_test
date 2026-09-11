namespace MyAvaloniaManagement.Icons;

/// <summary>仅在未来版本夹具存在的专属图形，不能依赖 Host 当前的公共名称目录。</summary>
public static class FutureIcon
{
    public static CommonIconAsset Asset { get; } = new("builtin:future-envelope", "M0,0 H32 V16 H0 Z", 32, 16);
}
