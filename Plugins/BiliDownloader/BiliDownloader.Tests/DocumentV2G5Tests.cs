using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using BiliDownloader.ViewModels.BiliDownloader;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace BiliDownloader.Tests;

/// <summary>
/// BiliDownloader 当前内容 schema 的保存、往返和严格拒绝测试。
/// </summary>
/// <remarks>
/// 文件名沿用已有测试分组，测试本身只承诺当前 V3。项目不存在旧 Document 信封，
/// 因而这里没有旧字段、版本文本或迁移夹具。
/// </remarks>
public class DocumentV2G5Tests
{
    private static BiliDownloaderViewModel CreateVm(ISettingsRepository? settings = null)
    {
        var messenger = new RecordingMessengerService();
        var taskRepo = new InMemoryDownloadTaskRepository();
        var settingsRepo = settings ?? new InMemorySettingsRepository();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            messenger);

        return new BiliDownloaderViewModel(
            messenger,
            taskRepo,
            settingsRepo,
            loginState,
            new BiliLoginService(),
            new BiliApiService(),
            new FakeCredentialProvider(),
            new FakeFfmpegService());
    }

    [Fact]
    public void 当前保存_使用独立整数内容Schema3()
    {
        var saveData = CreateVm().CreateSaveDocumentMetaData("unused.mamdoc");

        Assert.Equal(DocumentSaveCodec.CurrentContentSchemaVersion, saveData.ContentSchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(saveData.Payload));
    }

    [Fact]
    public void 当前保存_包含全部持久字段()
    {
        var vm = CreateVm();
        vm.NamingTemplate.Template = "{bv}_{title}";
        vm.DownloadConfig.DownloadDanmaku = true;
        vm.DownloadConfig.DownloadSubtitle = true;
        vm.DownloadConfig.DownloadCover = true;

        var saveData = vm.CreateSaveDocumentMetaData("unused.mamdoc");
        var content = JsonConvert.DeserializeObject<DocumentSaveDataV3>(saveData.Payload);

        Assert.NotNull(content);
        Assert.Equal("{bv}_{title}", content.NamingTemplate);
        Assert.True(content.DownloadDanmaku);
        Assert.True(content.DownloadSubtitle);
        Assert.True(content.DownloadCover);
    }

    [Fact]
    public void 当前保存加载_配置往返一致()
    {
        var source = CreateVm();
        source.NamingTemplate.Template = "{index}_{bv}_{title}";
        source.DownloadConfig.DownloadDanmaku = true;
        source.DownloadConfig.DownloadSubtitle = false;
        source.DownloadConfig.DownloadCover = true;

        var target = CreateVm();
        target.LoadDocumentByMetaData(source.CreateSaveDocumentMetaData("unused.mamdoc"));

        Assert.Equal("{index}_{bv}_{title}", target.NamingTemplate.Template);
        Assert.True(target.DownloadConfig.DownloadDanmaku);
        Assert.False(target.DownloadConfig.DownloadSubtitle);
        Assert.True(target.DownloadConfig.DownloadCover);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(99)]
    public void 非当前内容Schema_明确拒绝(int contentSchemaVersion)
    {
        var saveData = new DocumentSaveData(contentSchemaVersion, "{}");

        var exception = Assert.Throws<DocumentLoadException>(() =>
            CreateVm().LoadDocumentByMetaData(saveData));

        Assert.Equal("该 BiliDownloader Document 不是当前支持的 V3 格式。", exception.Message);
        Assert.DoesNotContain("{}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    public void 当前Schema正文损坏_返回稳定脱敏错误(string payload)
    {
        var saveData = new DocumentSaveData(
            DocumentSaveCodec.CurrentContentSchemaVersion,
            payload);

        var exception = Assert.Throws<DocumentLoadException>(() =>
            CreateVm().LoadDocumentByMetaData(saveData));

        if (payload.Length > 0)
        {
            Assert.DoesNotContain(payload, exception.Message, StringComparison.Ordinal);
        }
    }
}
