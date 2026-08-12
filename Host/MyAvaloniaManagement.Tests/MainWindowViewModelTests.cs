using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证主窗口 ViewModel 的布局、文件、消息和文档生命周期。
/// </summary>
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void 创建文档会加入文件停靠区()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();

        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);

        var dock = GetDocumentDock(context);
        Assert.Contains(dock.VisibleDockables!, item =>
            item is TestSavableDocument);
    }

    [Fact]
    public async Task 打开文档只读取一次并恢复保存数据()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "opened.testdoc");
        context.Storage.AddFile(path, Serialize("标题", "正文"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        var document = GetDocuments(context).Single();
        Assert.Equal(Path.GetFullPath(path), document.FilePath);
        Assert.Equal("标题", document.Title);
        Assert.Equal("正文", document.Content);
    }

    [Fact]
    public async Task 批量打开遇到重复和损坏文件仍继续后续文件()
    {
        using var context = CreateContextWithDocumentStrategy();
        var first = Path.Combine(context.TempDirectory, "first.testdoc");
        var broken = Path.Combine(context.TempDirectory, "broken.testdoc");
        var second = Path.Combine(context.TempDirectory, "second.testdoc");
        context.Storage.AddFile(first, Serialize("第一", "A"));
        context.Storage.AddFile(broken, "{broken");
        context.Storage.AddFile(second, Serialize("第二", "B"));
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.OpenDocumentByPath(first);
        context.Storage.OpenPaths = [first.ToUpperInvariant(), broken, second];

        await viewModel.OpenDocument();

        Assert.Equal(2, GetDocuments(context).Count);
        Assert.Contains(GetDocuments(context), item => item.Title == "第一");
        Assert.Contains(GetDocuments(context), item => item.Title == "第二");
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("原文件未被修改", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 不存在路径不会创建文档()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(
            Path.Combine(context.TempDirectory, "missing.testdoc"));

        Assert.Empty(GetDocuments(context));
    }

    [Fact]
    public async Task 未知文档类型不会阻止同批次有效文档()
    {
        using var context = CreateContextWithDocumentStrategy();
        var unknown = Path.Combine(context.TempDirectory, "unknown.testdoc");
        var valid = Path.Combine(context.TempDirectory, "valid.testdoc");
        context.Storage.AddFile(
            unknown,
            JsonConvert.SerializeObject(new DocumentSaveData
            {
                DocumentTypeId = new("unknown"),
                Title = "未知",
                Content = "",
                PluginMetadata = "",
                SaveTime = DateTime.UtcNow
            }));
        context.Storage.AddFile(valid, Serialize("有效", "ok"));
        context.Storage.OpenPaths = [unknown, valid];
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocument();

        Assert.Single(GetDocuments(context));
        Assert.Equal("有效", GetDocuments(context)[0].Title);
    }

    [Fact]
    public async Task 新文档保存使用元数据并同步标题路径()
    {
        using var context = CreateContextWithDocumentStrategy();
        var savePath = Path.Combine(context.TempDirectory, "saved.testdoc");
        context.Storage.SavePath = savePath;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        GetDocumentDock(context).ActiveDockable = document;
        document.Content = "保存内容";

        await viewModel.SaveDocument();

        Assert.Equal(TestSavableStrategy.TypeId,
            context.Storage.LastSaveMetadata?.DocumentTypeId);
        Assert.Equal(Path.GetFullPath(savePath), document.FilePath);
        Assert.Equal("saved", document.Title);
        var stored = JsonConvert.DeserializeObject<DocumentSaveData>(
            Assert.Single(context.Storage.Writes).Content);
        Assert.Equal("saved", stored?.Title);
        Assert.Equal("保存内容", stored?.Content);
    }

    [Fact]
    public async Task 取消保存不会写入()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        GetDocumentDock(context).ActiveDockable =
            GetDocuments(context).Single();

        await viewModel.SaveDocument();

        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 已有路径直接覆盖且不再打开保存对话框()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "existing.testdoc");
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.FilePath = path;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Null(context.Storage.LastSaveMetadata);
        Assert.Equal(Path.GetFullPath(path),
            Assert.Single(context.Storage.Writes).Path);
        Assert.Equal(1, document.SaveCompletedCount);
    }

    [Fact]
    public async Task 受保护文档_强制另存并在写入成功后解除保护()
    {
        using var context = CreateContextWithDocumentStrategy();
        var original = Path.Combine(context.TempDirectory, "future.testdoc");
        var copy = Path.Combine(context.TempDirectory, "future-copy.testdoc");
        context.Storage.SavePath = copy;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.FilePath = original;
        document.RequiresSaveAs = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Equal(Path.GetFullPath(copy), Assert.Single(context.Storage.Writes).Path);
        Assert.False(document.RequiresSaveAs);
        Assert.Equal(1, document.SaveCompletedCount);
    }

    [Fact]
    public async Task 受保护文档_选择原路径时拒绝覆盖()
    {
        using var context = CreateContextWithDocumentStrategy();
        var original = Path.Combine(context.TempDirectory, "future.testdoc");
        context.Storage.SavePath = original;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.FilePath = original;
        document.RequiresSaveAs = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Empty(context.Storage.Writes);
        Assert.True(document.RequiresSaveAs);
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("不同的文件路径", viewModel.DocumentOperationError);
    }

    [Fact]
    public void 更新布局消息触发布局属性通知()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        context.Messenger.Send(new UpdateLayoutMessage("refresh"));

        Assert.Contains(nameof(viewModel.Layout), changed);
    }

    [Fact]
    public void 打开文件消息调用路径打开流程()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "message.testdoc");
        context.Storage.AddFile(path, Serialize("消息", "content"));
        _ = context.CreateMainWindowViewModel();

        context.Messenger.Send(new OpenFileMessage(path));

        Assert.Single(GetDocuments(context));
    }

    [Fact]
    public void 主题命令更新单选状态并立即持久化()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.SetThemeCommand.Execute("Dark");

        Assert.False(viewModel.IsSystemTheme);
        Assert.False(viewModel.IsLightTheme);
        Assert.True(viewModel.IsDarkTheme);
        Assert.True(File.Exists(context.AppearanceSettingsPath));
        Assert.Contains(nameof(viewModel.IsSystemTheme), changed);
        Assert.Contains(nameof(viewModel.IsLightTheme), changed);
        Assert.Contains(nameof(viewModel.IsDarkTheme), changed);
    }

    [Fact]
    public async Task ConcurrentOpenOfSamePathCreatesOneDocumentAndReadsOnce()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "concurrent.testdoc");
        context.Storage.AddFile(path, Serialize("Concurrent", "content"));
        var viewModel = context.CreateMainWindowViewModel();

        await Task.WhenAll(
            viewModel.OpenDocumentByPath(path),
            viewModel.OpenDocumentByPath(path));

        Assert.Single(GetDocuments(context));
        Assert.Equal(1, context.Storage.ReadCount);
    }

    [Fact]
    public async Task SaveFailureDoesNotMutateDocumentState()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "failed.testdoc");
        context.Storage.SavePath = path;
        context.Storage.WriteException = new IOException("simulated");
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        var originalTitle = document.Title;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Equal(originalTitle, document.Title);
        Assert.Equal(string.Empty, document.FilePath);
        Assert.Equal(0, document.SaveCompletedCount);
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public void OpenMessageObservesExpectedReadFailure()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "message-failure.testdoc");
        context.Storage.AddFile(path, Serialize("Failure", "content"));
        context.Storage.ReadException = new IOException("simulated");
        var viewModel = context.CreateMainWindowViewModel();

        context.Messenger.Send(new OpenFileMessage(path));

        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Empty(GetDocuments(context));
    }

    private static TestHostContext CreateContextWithDocumentStrategy()
    {
        return new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
    }

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<IDocumentDock>("Files"));

    private static List<TestSavableDocument> GetDocuments(
        TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<TestSavableDocument>()
            .ToList();

    private static string Serialize(string title, string content) =>
        JsonConvert.SerializeObject(new DocumentSaveData
        {
            DocumentTypeId = TestSavableStrategy.TypeId,
            Title = title,
            Content = content,
            PluginMetadata = """{"test":true}""",
            SaveTime = DateTime.UtcNow
        });
}
