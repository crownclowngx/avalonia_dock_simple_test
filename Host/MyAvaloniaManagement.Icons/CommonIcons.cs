namespace MyAvaloniaManagement.Icons;

/// <summary>只包含基础数据的公共图标资产；所有者在自己的加载上下文中读取它。</summary>
/// <remarks>
/// 本类型不作为插件注册契约传递。使用者把字符串与画布尺寸复制成 SDK 描述，
/// 因此 Host 与多个插件可以持有不同版本的资源包，不需要共享本程序集的类型身份。
/// </remarks>
public sealed class CommonIconAsset
{
    internal CommonIconAsset(string key, string pathData, double width, double height)
    {
        Key = key;
        PathData = pathData;
        ViewBoxWidth = width;
        ViewBoxHeight = height;
    }

    /// <summary>获取由 Host 导入的稳定公共名称；旧 Host 不一定拥有新资源包新增的名称。</summary>
    public string Key { get; }
    /// <summary>获取使用 EvenOdd 填充规则的矢量路径，不包含文件或网络地址。</summary>
    public string PathData { get; }
    /// <summary>获取以零为原点的逻辑画布宽度。</summary>
    public double ViewBoxWidth { get; }
    /// <summary>获取以零为原点的逻辑画布高度。</summary>
    public double ViewBoxHeight { get; }
}

/// <summary>Host 与插件可独立引用的固定图形资源；只公开不可变资产，不包含可写注册表。</summary>
/// <remarks>图形延续本仓 V6 的几何数据，统一使用 20×20 逻辑画布。新增资源同时加入 All，Host 自动导入。</remarks>
public static class CommonIcons
{
    /// <summary>公共默认四宫格。</summary>
    public static CommonIconAsset Module { get; } = Asset("module", "M2,2 H8 V8 H2 Z M10,2 H16 V8 H10 Z M2,10 H8 V16 H2 Z M10,10 H16 V16 H10 Z");
    /// <summary>分类文件夹。</summary>
    public static CommonIconAsset Folder { get; } = Asset("folder", "M2,4 H8 L10,6 H18 V8 H5 L3,16 H1 Z M5,9 H19 L16,17 H2 Z");
    /// <summary>表格与批量数据。</summary>
    public static CommonIconAsset Table { get; } = Asset("table", "M2,2 H18 V18 H2 Z M4,6 V10 H9 V6 Z M11,6 V10 H16 V6 Z M4,12 V16 H9 V12 Z M11,12 V16 H16 V12 Z");
    /// <summary>统计图表。</summary>
    public static CommonIconAsset Chart { get; } = Asset("chart", "M2,2 H4 V16 H18 V18 H2 Z M6,10 H8 V14 H6 Z M10,6 H12 V14 H10 Z M14,3 H16 V14 H14 Z");
    /// <summary>文本审核。</summary>
    public static CommonIconAsset TextCheck { get; } = Asset("text-check", "M2,3 H17 V5 H2 Z M2,7 H12 V9 H2 Z M2,11 H8 V13 H2 Z M9,14 L11,12 L14,15 L18,10 L20,12 L14,19 Z");
    /// <summary>图片。</summary>
    public static CommonIconAsset Image { get; } = Asset("image", "M1,2 H19 V18 H1 Z M3,4 V15 L8,9 L11,12 L14,8 L17,12 V4 Z M5,5 H8 V8 H5 Z");
    /// <summary>视频。</summary>
    public static CommonIconAsset Video { get; } = Asset("video", "M2,3 H14 V7 L19,4 V16 L14,13 V17 H2 Z M6,6 V14 L12,10 Z");
    /// <summary>下载。</summary>
    public static CommonIconAsset Download { get; } = Asset("download", "M8,1 H12 V10 H16 L10,16 L4,10 H8 Z M2,16 H4 V18 H16 V16 H18 V20 H2 Z");
    /// <summary>获取全部公共图形的只读目录；顺序稳定，名称唯一。</summary>
    public static IReadOnlyList<CommonIconAsset> All { get; } =
        Array.AsReadOnly(new[] { Module, Folder, Table, Chart, TextCheck, Image, Video, Download });

    private static CommonIconAsset Asset(string name, string data) => new($"builtin:{name}", data, 20, 20);
}
