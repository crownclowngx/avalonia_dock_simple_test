using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;
using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// G8 保存契约与宿主运行期持久化状态的专项门禁。
/// </summary>
public sealed class DocumentContentContractTests
{
    [Fact]
    public void 宿主状态_创建时绑定规范注册项且路径只在宿主提交()
    {
        using var context = new TestHostContext([new TestSavableStrategy()]);
        var document = context.Factory.CreateManagementNewDocument(
            new DocumentCreationParams(TestSavableStrategy.TypeId));

        Assert.True(context.PersistenceStates.TryGet(document, out var state));
        Assert.Equal(
            TestSavableStrategy.TypeId.Value,
            state.Registration.Descriptor.DocumentTypeId.Value);
        Assert.Equal(string.Empty, state.FilePath);

        var relativePath = Path.Combine(context.TempDirectory, "folder", "..", "state.mamdoc");
        context.PersistenceStates.CommitFilePath(document, relativePath);

        Assert.Equal(Path.GetFullPath(relativePath), state.FilePath);
        context.Factory.ReleaseDocument(document);
        Assert.False(context.PersistenceStates.TryGet(document, out _));
    }

    [Fact]
    public void 宿主状态_重复登记拒绝且删除幂等()
    {
        using var context = new TestHostContext([new TestSavableStrategy()]);
        var document = context.Factory.CreateManagementNewDocument(
            new DocumentCreationParams(TestSavableStrategy.TypeId));
        Assert.True(context.PersistenceStates.TryGet(document, out var state));

        Assert.Throws<InvalidOperationException>(() =>
            context.PersistenceStates.Register(document, state.Registration));
        Assert.True(context.PersistenceStates.Remove(document));
        Assert.False(context.PersistenceStates.Remove(document));

        context.Factory.ReleaseDocument(document);
    }

    [Fact]
    public void 创建策略重复返回同一引用_拒绝新请求且保留原宿主状态()
    {
        var strategy = new ReusedSavableStrategy();
        using var context = new TestHostContext([strategy]);
        var first = context.Factory.CreateManagementNewDocument(
            new DocumentCreationParams(ReusedSavableStrategy.TypeId));
        var committedPath = Path.Combine(context.TempDirectory, "existing.mamdoc");
        context.PersistenceStates.CommitFilePath(first, committedPath);

        Assert.Throws<InvalidOperationException>(() =>
            context.Factory.CreateManagementNewDocument(
                new DocumentCreationParams(ReusedSavableStrategy.TypeId)));

        Assert.True(context.PersistenceStates.TryGet(first, out var state));
        Assert.Equal(Path.GetFullPath(committedPath), state.FilePath);
        context.Factory.ReleaseDocument(first);
    }

    [Fact]
    public void 创建内容快照_不改变标题脏状态或宿主路径()
    {
        using var context = new TestHostContext([new TestSavableStrategy()]);
        var document = Assert.IsType<TestSavableDocument>(
            context.Factory.CreateManagementNewDocument(
                new DocumentCreationParams(TestSavableStrategy.TypeId)));
        document.Title = "快照前标题";
        document.Content = "快照正文";
        document.IsModified = true;
        context.SetDocumentFilePath(document, Path.Combine(context.TempDirectory, "before.mamdoc"));

        var snapshot = document.CreateContentSnapshot();

        Assert.Equal(1, snapshot.ContentSchemaVersion);
        Assert.Equal("快照正文", snapshot.Payload);
        Assert.Equal("快照前标题", document.Title);
        Assert.True(document.IsDirty);
        Assert.EndsWith("before.mamdoc", context.GetDocumentFilePath(document));
        context.Factory.ReleaseDocument(document);
    }

    private sealed class ReusedSavableStrategy : IDocumentCreationStrategy
    {
        internal static readonly DocumentTypeId TypeId =
            new("myavalonia.host.document.reused-savable");
        private readonly TestSavableDocument _document = new();

        public Document CreateDocument(DocumentCreationParams @params) => _document;

        public DocumentMetadata GetMetadata() => new(TypeId, "重复引用测试");
    }
}
