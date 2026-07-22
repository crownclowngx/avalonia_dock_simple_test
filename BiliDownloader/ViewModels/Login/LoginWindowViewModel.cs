using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.ViewModels.Login;

/// <summary>
/// 登录弹窗 ViewModel：管理二维码生成、扫码轮询、登录成功后的状态传递。
/// </summary>
public partial class LoginWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _qrCodeImage;

    [ObservableProperty]
    private bool _hasQrCode;

    [ObservableProperty]
    private string _statusText = "正在生成二维码...";

    [ObservableProperty]
    private bool _isPolling;

    /// <summary>
    /// 登录成功时为 true，调用方可据此关闭弹窗
    /// </summary>
    public bool LoginSuccess { get; private set; }

    private string _qrCodeKey = string.Empty;
    private CancellationTokenSource? _pollCts;
    private readonly BiliLoginService _loginService;
    private readonly BiliLoginStateService _loginStateService;

    public IAsyncRelayCommand LoadQrCodeCommand { get; }

    public LoginWindowViewModel(
        BiliLoginService loginService,
        BiliLoginStateService loginStateService)
    {
        _loginService = loginService;
        _loginStateService = loginStateService;
        LoadQrCodeCommand = new AsyncRelayCommand(LoadQrCodeAsync);
    }

    /// <summary>
    /// 生成二维码并启动轮询
    /// </summary>
    private async Task LoadQrCodeAsync()
    {
        try
        {
            StatusText = "正在生成二维码...";
            HasQrCode = false;
            QrCodeImage = null;

            // 通过 BiliLoginService 获取二维码 URL
            var service = _loginService;
            var (url, key) = await service.GetQrCodeAsync();
            _qrCodeKey = key;

            // 用 QRCoder 生成 PNG 字节（明确黑色像素 + 白色背景，确保高对比度）
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var pngByteQRCode = new PngByteQRCode(qrCodeData);
            var pngBytes = pngByteQRCode.GetGraphic(
                pixelsPerModule: 8,
                darkColorRgba: new byte[] { 0, 0, 0, 255 },
                lightColorRgba: new byte[] { 255, 255, 255, 255 },
                drawQuietZones: true);

            // 转为 Avalonia Bitmap
            await using var ms = new MemoryStream(pngBytes);
            QrCodeImage = new Bitmap(ms);
            HasQrCode = true;

            StatusText = "请使用 B站 APP 扫描二维码登录";
            IsPolling = true;

            // 启动轮询
            _pollCts = new CancellationTokenSource();
            await PollAsync(_pollCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 用户取消，静默退出
        }
        catch (Exception ex)
        {
            StatusText = $"获取二维码失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}";
        }
    }

    /// <summary>
    /// 轮询扫码结果
    /// </summary>
    private async Task PollAsync(CancellationToken ct)
    {
        var service = _loginService;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            try
            {
                var (status, cookies) = await service.PollQrCodeAsync(_qrCodeKey);
                switch (status)
                {
                    case BiliLoginService.QrCodeStatus.Success:
                        IsPolling = false;
                        // 通过插件级单例状态服务完成登录，避免弹窗自行创建第二套登录状态。
                        LoginSuccess = await _loginStateService.LoginAsync(cookies);
                        StatusText = LoginSuccess
                            ? _loginStateService.StatusMessage ?? "登录成功！"
                            : _loginStateService.StatusMessage ?? "登录失败，请重试。";
                        return;

                    case BiliLoginService.QrCodeStatus.ScannedPending:
                        StatusText = "已扫码，请在手机上确认...";
                        break;

                    case BiliLoginService.QrCodeStatus.Expired:
                        StatusText = "二维码已过期，请点击刷新";
                        IsPolling = false;
                        return;

                    case BiliLoginService.QrCodeStatus.WaitingForScan:
                        // 继续等待
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"轮询异常：{SensitiveDataSanitizer.Sanitize(ex.Message)}，2秒后重试...";
            }
        }
    }

    /// <summary>
    /// 取消轮询（窗口关闭时调用）
    /// </summary>
    public void CancelPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }
}
