using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using BiliDownloader.ViewModels.BiliDownloader;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Tests;

/// <summary>
/// 当前 Document 保存格式及非当前格式拒绝测试。
/// 文件名来自原测试分组；测试语义只覆盖当前 V3，不提供 V1/V2 兼容保证。
/// </summary>
public class DocumentV2G5Tests
{
    /// <summary>
    /// 创建测试用的 BiliDownloaderViewModel 实例。
    /// </summary>
    private static BiliDownloaderViewModel CreateVm(ISettingsRepository? settings = null)
    {
        var messenger = new RecordingMessengerService();
        var taskRepo = new InMemoryDownloadTaskRepository();
        var settingsRepo = settings ?? new InMemorySettingsRepository();
        var loginState = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            messenger);
        var ffmpeg = new FakeFfmpegService();

        return new BiliDownloaderViewModel(
            messenger,
            taskRepo,
            settingsRepo,
            loginState,
            new BiliLoginService(),
            new BiliApiService(),
            new FakeCredentialProvider(),
            ffmpeg);
    }

    #region 当前格式保存与加载往返

    [Fact]
    public void 当前保存_PluginMetadata版本为3()
    {
        var vm = CreateVm();
        var saveData = vm.CreateSaveDocumentMetaData("test.doc");

        var metadata = JsonConvert.DeserializeObject<JObject>(saveData.PluginMetadata);
        Assert.Equal("3.0", metadata?["Version"]?.ToString());
    }

    [Fact]
    public void 当前保存_包含全部持久字段()
    {
        var vm = CreateVm();
        vm.NamingTemplate.Template = "{bv}_{title}";
        vm.DownloadConfig.DownloadDanmaku = true;
        vm.DownloadConfig.DownloadSubtitle = true;
        vm.DownloadConfig.DownloadCover = true;

        var saveData = vm.CreateSaveDocumentMetaData("test.doc");
        var content = JsonConvert.DeserializeObject<DocumentSaveDataV3>(saveData.Content);

        Assert.NotNull(content);
        Assert.Equal("{bv}_{title}", content.NamingTemplate);
        Assert.True(content.DownloadDanmaku);
        Assert.True(content.DownloadSubtitle);
        Assert.True(content.DownloadCover);
    }

    [Fact]
    public void 当前保存加载_命名模板往返一致()
    {
        var vm = CreateVm();
        vm.NamingTemplate.Template = "{index}_{bv}_{title}";

        var saveData = vm.CreateSaveDocumentMetaData("test.doc");

        var vm2 = CreateVm();
        vm2.LoadDocumentByMetaData(saveData);

        Assert.Equal("{index}_{bv}_{title}", vm2.NamingTemplate.Template);
    }

    [Fact]
    public void 当前保存加载_附加资源配置往返一致()
    {
        var vm = CreateVm();
        vm.DownloadConfig.DownloadDanmaku = true;
        vm.DownloadConfig.DownloadSubtitle = false;
        vm.DownloadConfig.DownloadCover = true;

        var saveData = vm.CreateSaveDocumentMetaData("test.doc");

        var vm2 = CreateVm();
        vm2.LoadDocumentByMetaData(saveData);

        Assert.True(vm2.DownloadConfig.DownloadDanmaku);
        Assert.False(vm2.DownloadConfig.DownloadSubtitle);
        Assert.True(vm2.DownloadConfig.DownloadCover);
    }

    #endregion

    #region 非当前格式拒绝

    [Fact]
    public void V1格式_不执行默认值迁移()
    {
        // 构造 V1 格式的保存数据
        var v1Content = new
        {
            DocumentId = "doc-v1",
            Url = "https://bilibili.com/video/BV1test",
            DownloadInfo = "",
            OutputDirectory = "C:\\Videos",
            UseGroupFolder = true,
            AddIndexToTitle = true
        };

        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void V1格式_不迁移命名模板()
    {
        var v1Content = new
        {
            DocumentId = "doc-v1",
            Url = "",
            DownloadInfo = "",
            OutputDirectory = "",
            UseGroupFolder = false,
            AddIndexToTitle = false
        };

        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void V1格式_不猜测恢复旧字段()
    {
        var v1Content = new
        {
            DocumentId = "my-doc-id",
            Url = "https://bilibili.com/video/BV1abc",
            DownloadInfo = "日志内容",
            OutputDirectory = "D:\\Downloads",
            UseGroupFolder = true,
            AddIndexToTitle = true
        };

        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    #endregion

    #region 版本判别

    [Fact]
    public void 未知版本_明确拒绝()
    {
        var content = new { DocumentId = "doc-future", Url = "https://test.com" };
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "99.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void PluginMetadata为空_不猜测为旧版本()
    {
        var content = new { DocumentId = "doc-no-meta", Url = "https://test.com" };
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(content),
            PluginMetadata = ""
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void 非V3且Content为空_明确拒绝()
    {
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = "",
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    [Fact]
    public void 非V3且Content为null_明确拒绝()
    {
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = new("bilitools.bilidownloader"),
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = null!,
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" })
        };

        var vm = CreateVm();
        Assert.Throws<DocumentLoadException>(() =>
            vm.LoadDocumentByMetaData(saveData));
    }

    #endregion

}
