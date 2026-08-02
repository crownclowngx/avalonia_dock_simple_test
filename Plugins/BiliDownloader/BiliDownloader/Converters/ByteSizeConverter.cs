using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BiliDownloader.Converters;

/// <summary>
/// G4: 字节数到人类可读格式的转换器。
/// 设计思考：任务中心需要展示"预计大小"和"已下载大小"，
/// 原始字节数（如 1073741824）对用户无意义，需要格式化为 "1.0 GB"。
/// 复用现有 IValueConverter 模式（与 TaskControlConverters.cs 一致），
/// 不引入第三方格式化库。
/// </summary>
public class ByteSizeConverter : IValueConverter
{
    /// <summary>
    /// 将 long 类型字节数转换为人类可读字符串。
    /// 格式规则：
    /// - 0 或负数 → "0 B"
    /// - < 1024 → "N B"
    /// - < 1024² → "N.N KB"
    /// - < 1024³ → "N.N MB"
    /// - ≥ 1024³ → "N.NN GB"（GB 保留两位小数，因为视频文件通常较大）
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 支持 long 和 int 两种输入（XAML 绑定可能传递不同数值类型）
        long bytes = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0,
        };

        return FormatBytes(bytes);
    }

    /// <summary>
    /// 将字节数格式化为人类可读字符串（纯函数，可独立测试）。
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        // 使用 1024 进制（与操作系统文件管理器一致）
        const double kb = 1024;
        const double mb = 1024 * 1024;
        const double gb = 1024 * 1024 * 1024;

        return bytes switch
        {
            < (long)kb => $"{bytes} B",
            < (long)mb => $"{bytes / kb:F1} KB",
            < (long)gb => $"{bytes / mb:F1} MB",
            _ => $"{bytes / gb:F2} GB",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
