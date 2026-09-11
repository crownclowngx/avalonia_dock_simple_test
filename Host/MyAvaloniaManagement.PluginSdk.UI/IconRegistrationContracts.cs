namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>定义单色矢量轮廓的固定填充规则。</summary>
public enum IconFillRule
{
    /// <summary>按轮廓交叉次数奇偶填充，适合镂空图形。</summary>
    EvenOdd,
    /// <summary>按非零绕数填充。</summary>
    NonZero,
}

/// <summary>跨插件边界传递的不可变图形定义，不依赖任何资源包私有类型。</summary>
/// <remarks>
/// 构造只检查基础数据；路径解析由 Host 的 UI 渲染层在 Avalonia 就绪后完成。
/// 插件注册时不创建 Geometry、Control、Provider 或回调，浏览目录也不会触发页面初始化。
/// </remarks>
public sealed class VectorIconDefinition
{
    /// <summary>创建原点为零的逻辑画布及单色矢量定义。</summary>
    /// <param name="pathData">非空白矢量路径正文，不是图片路径或 URL。</param>
    /// <param name="viewBoxWidth">有限且大于零的逻辑画布宽度。</param>
    /// <param name="viewBoxHeight">有限且大于零的逻辑画布高度。</param>
    /// <param name="fillRule">固定填充规则，默认为 EvenOdd。</param>
    /// <exception cref="ArgumentException">路径为空白。</exception>
    /// <exception cref="ArgumentNullException">路径为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">尺寸或枚举无效。</exception>
    public VectorIconDefinition(string pathData, double viewBoxWidth, double viewBoxHeight,
        IconFillRule fillRule = IconFillRule.EvenOdd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathData);
        if (!double.IsFinite(viewBoxWidth) || viewBoxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewBoxWidth));
        if (!double.IsFinite(viewBoxHeight) || viewBoxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewBoxHeight));
        if (!Enum.IsDefined(fillRule)) throw new ArgumentOutOfRangeException(nameof(fillRule));
        PathData = pathData;
        ViewBoxWidth = viewBoxWidth;
        ViewBoxHeight = viewBoxHeight;
        FillRule = fillRule;
    }
    /// <summary>获取矢量路径正文。</summary>
    public string PathData { get; }
    /// <summary>获取逻辑画布宽度。</summary>
    public double ViewBoxWidth { get; }
    /// <summary>获取逻辑画布高度。</summary>
    public double ViewBoxHeight { get; }
    /// <summary>获取轮廓填充规则。</summary>
    public IconFillRule FillRule { get; }
}

/// <summary>由 Host 提供的可选图标注册能力，不扩大已发布的 IPluginRegistration 抽象方法集合。</summary>
public interface IPluginIconRegistration
{
    /// <summary>在当前模块注册窗口内声明专属图标，返回绑定真实插件身份的完整引用。</summary>
    /// <param name="localName">小写字母开头、字母数字及单个连字符分段的局部名称。</param>
    /// <param name="definition">只读矢量数据。</param>
    /// <returns>可用于已有 IconPath 字段的 plugin:所有者/局部名称。</returns>
    string AddIcon(string localName, VectorIconDefinition definition);
}

/// <summary>使普通模块显式请求图标贡献能力；不支持的 Host 不会伪造注册成功。</summary>
public static class IconRegistrationExtensions
{
    /// <summary>声明当前插件拥有的图标，不传入可伪造的所有者参数。</summary>
    /// <param name="registration">当前模块的注册入口。</param>
    /// <param name="localName">图标局部名称。</param>
    /// <param name="definition">不可变矢量定义。</param>
    /// <returns>Host 生成的完整 IconPath。</returns>
    /// <exception cref="ArgumentNullException">注册入口或定义为 null。</exception>
    /// <exception cref="NotSupportedException">Host 尚未提供图标贡献能力。</exception>
    public static string AddIcon(this IPluginRegistration registration, string localName, VectorIconDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(definition);
        return (registration as IPluginIconRegistration ??
            throw new NotSupportedException("当前 Host 不支持图标注册，需要 Plugin SDK/Host 契约 3.4.0 或更高版本。"))
            .AddIcon(localName, definition);
    }
}
