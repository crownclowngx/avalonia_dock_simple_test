using QRCoder;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 将登录地址编码为可直接交给界面层解码的 PNG。
/// </summary>
internal static class QrCodePngEncoder
{
    /// <summary>
    /// 生成带静区的高对比度二维码 PNG。
    /// </summary>
    public static byte[] Encode(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // 二维码编码是无状态的纯计算，保持为内部帮助类即可；当前没有第二种实现，
        // 因此不额外引入接口和依赖注入，避免让登录编排承担图像编码细节。
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var pngByteQrCode = new PngByteQRCode(qrCodeData);
        return pngByteQrCode.GetGraphic(
            pixelsPerModule: 8,
            darkColorRgba: [0, 0, 0, 255],
            lightColorRgba: [255, 255, 255, 255],
            drawQuietZones: true);
    }
}
