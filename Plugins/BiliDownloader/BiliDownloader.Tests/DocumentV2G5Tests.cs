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
/// G5 Document V2 保存格式测试。
/// 覆盖：V2 保存/加载往返、V1 兼容加载、版本判别。
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

    #region V2 保存→加载往返

    [Fact]
    public void V2保存_PluginMetadata版本为2()
    {
        var vm = CreateVm();
        var saveData = vm.CreateSaveDocumentMetaData("test.doc");

        var metadata = JsonConvert.DeserializeObject<JObject>(saveData.PluginMetadata);
        Assert.Equal("2.0", metadata?["Version"]?.ToString());
    }

    [Fact]
    public void V2保存_包含所有新增字段()
    {
        var vm = CreateVm();
        vm.NamingTemplate.Template = "{bv}_{title}";
        vm.DownloadConfig.DownloadDanmaku = true;
        vm.DownloadConfig.DownloadSubtitle = true;
        vm.DownloadConfig.DownloadCover = true;

        var saveData = vm.CreateSaveDocumentMetaData("test.doc");
        var content = JsonConvert.DeserializeObject<DocumentSaveDataV2>(saveData.Content);

        Assert.NotNull(content);
        Assert.Equal("{bv}_{title}", content.NamingTemplate);
        Assert.True(content.DownloadDanmaku);
        Assert.True(content.DownloadSubtitle);
        Assert.True(content.DownloadCover);
    }

    [Fact]
    public void V2保存加载_命名模板往返一致()
    {
        var vm = CreateVm();
        vm.NamingTemplate.Template = "{index}_{bv}_{title}";

        var saveData = vm.CreateSaveDocumentMetaData("test.doc");

        var vm2 = CreateVm();
        vm2.LoadDocumentByMetaData(saveData);

        Assert.Equal("{index}_{bv}_{title}", vm2.NamingTemplate.Template);
    }

    [Fact]
    public void V2保存加载_附加资源配置往返一致()
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

    #region V1 兼容加载

    [Fact]
    public void V1加载_补齐命名模板默认值()
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
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        vm.LoadDocumentByMetaData(saveData);

        // V1 AddIndexToTitle=true → 模板应为 "{index}.{title}"
        Assert.Equal("{index}.{title}", vm.NamingTemplate.Template);
        Assert.Equal("C:\\Videos", vm.DownloadConfig.OutputDirectory);
        Assert.True(vm.DownloadConfig.UseGroupFolder);
    }

    [Fact]
    public void V1加载_AddIndexToTitle为false_模板为title()
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
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        vm.LoadDocumentByMetaData(saveData);

        Assert.Equal("{title}", vm.NamingTemplate.Template);
    }

    [Fact]
    public void V1加载_原有字段不丢失()
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
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(v1Content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        var vm = CreateVm();
        vm.LoadDocumentByMetaData(saveData);

        Assert.Equal("my-doc-id", vm.DocumentId);
        Assert.Equal("https://bilibili.com/video/BV1abc", vm.VideoParse.Url);
        Assert.Equal("D:\\Downloads", vm.DownloadConfig.OutputDirectory);
        Assert.True(vm.DownloadConfig.UseGroupFolder);
    }

    #endregion

    #region 版本判别

    [Fact]
    public void 未知版本_宽容读取不崩溃()
    {
        var content = new { DocumentId = "doc-future", Url = "https://test.com" };
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(content),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "99.0" })
        };

        var vm = CreateVm();
        // 不应抛出异常
        vm.LoadDocumentByMetaData(saveData);
    }

    [Fact]
    public void PluginMetadata为空_回退V1()
    {
        var content = new { DocumentId = "doc-no-meta", Url = "https://test.com" };
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(content),
            PluginMetadata = ""
        };

        var vm = CreateVm();
        vm.LoadDocumentByMetaData(saveData);
        // 应使用 V1 默认模板
        Assert.Equal("{index}.{title}", vm.NamingTemplate.Template);
    }

    [Fact]
    public void Content为空_不崩溃()
    {
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = "",
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" })
        };

        var vm = CreateVm();
        // 不应抛出异常
        vm.LoadDocumentByMetaData(saveData);
    }

    [Fact]
    public void Content为null_不崩溃()
    {
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = "bilitools.bilidownloader",
            Title = "测试",
            SaveTime = DateTime.Now,
            Content = null!,
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" })
        };

        var vm = CreateVm();
        vm.LoadDocumentByMetaData(saveData);
    }

    #endregion

    #region DocumentSaveDataV2 模型

    [Fact]
    public void V2模型_默认值正确()
    {
        var dto = new DocumentSaveDataV2();
        Assert.Equal(BuiltInPresets.CompatId, dto.PresetId);
        Assert.Equal("{index}.{title}", dto.NamingTemplate);
        Assert.True(dto.AddIndexToTitle);
        Assert.False(dto.DownloadDanmaku);
        Assert.False(dto.DownloadSubtitle);
        Assert.False(dto.DownloadCover);
    }

    [Fact]
    public void V2模型_缺失字段反序列化补默认值()
    {
        // 模拟旧版本 JSON（缺少 V2 新增字段）
        var json = """{"DocumentId":"test","Url":"https://x.com"}""";
        var dto = JsonConvert.DeserializeObject<DocumentSaveDataV2>(json);

        Assert.NotNull(dto);
        Assert.Equal("test", dto.DocumentId);
        Assert.Equal(BuiltInPresets.CompatId, dto.PresetId);
        Assert.Equal("{index}.{title}", dto.NamingTemplate);
    }

    #endregion
}
