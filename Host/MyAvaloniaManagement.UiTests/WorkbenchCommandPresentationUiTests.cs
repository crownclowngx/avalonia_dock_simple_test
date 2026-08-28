using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>使用生产 XAML 和真实 Headless 输入验证 G4 打开/保存首个用户闭环。</summary>
public sealed class WorkbenchCommandPresentationUiTests
{
    private static readonly PluginId Owner =
        new("myavalonia.plugin.g4-ui-tests");
    private static readonly DocumentTypeId DocumentType =
        new("myavalonia.plugin.g4-ui-tests.document.persistable");
    private static readonly DocumentTypeId NonPersistableDocumentType =
        new("myavalonia.plugin.g4-ui-tests.document.non-persistable");

    [AvaloniaFact]
    public void 文件菜单和CtrlS绑定同一稳定保存命令且设计数据保持纯内存()
    {
        using var context = CreateContext();
        var menu = new MenuView { DataContext = context.ViewModel };
        var menuHost = new Window { Content = menu };
        menuHost.Show();
        var window = new MainWindow { DataContext = context.ViewModel };

        var items = menu.GetLogicalDescendants().OfType<MenuItem>().ToArray();
        var openItem = Assert.Single(items, item => Equals(item.Header, "打开"));
        var saveItem = Assert.Single(items, item => Equals(item.Header, "保存 (ctrl+s)"));
        var binding = Assert.Single(window.KeyBindings);
        var open = Assert.IsType<WorkbenchPresentationCommand>(openItem.Command);
        var save = Assert.IsType<WorkbenchPresentationCommand>(saveItem.Command);

        Assert.Equal(HostWorkbenchCommandIds.OpenDocument, open.CommandId);
        Assert.Equal(HostWorkbenchCommandIds.SaveDocument, save.CommandId);
        Assert.Same(save, binding.Command);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), binding.Gesture);
        Assert.True(openItem.IsEnabled);
        Assert.False(saveItem.IsEnabled);

        var design = new ViewModels.Design.MainWindowDesignData();
        Assert.NotNull(design.WorkbenchCommands.Open);
        Assert.NotNull(design.WorkbenchCommands.Save);
        Assert.IsNotType<WorkbenchPresentationCommand>(design.WorkbenchCommands.Open);
        Assert.IsNotType<WorkbenchPresentationCommand>(design.WorkbenchCommands.Save);

        menuHost.Close();
    }

    [AvaloniaFact]
    public async Task 保存菜单随活动目标更新且真实CtrlS只保存当前可持久化Document()
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        var saveItem = window.GetLogicalDescendants()
            .OfType<MenuItem>()
            .Single(item => Equals(item.Header, "保存 (ctrl+s)"));

        // 无活动 Document 时快捷键必须无副作用。
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        Assert.Empty(context.Storage.Writes);
        Assert.False(saveItem.IsEnabled);

        var nonPersistableDocument = await CreateDocumentAsync(
            context,
            NonPersistableDocumentType);
        var dock = GetDocumentDock(context);
        dock.ActiveDockable = nonPersistableDocument;
        Assert.False(saveItem.IsEnabled);
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        Assert.Empty(context.Storage.Writes);

        var document = await CreateDocumentAsync(context);
        dock.ActiveDockable = document;
        Assert.True(saveItem.IsEnabled);

        var path = Path.Combine(context.TempDirectory, "ctrl-s-save.mamdoc");
        context.Storage.SavePath = path;
        context.Storage.WriteObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.IsType<G4UiPersistableDocument>(document.Model).Edit("Ctrl+S 保存内容");

        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        await context.Storage.WriteObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(context.Storage.Writes, item =>
            string.Equals(item.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        Assert.False(Assert.IsType<G4UiPersistableDocument>(document.Model).IsDirty);

        dock.ActiveDockable = null;
        Assert.False(saveItem.IsEnabled);
        var writeCount = context.Storage.Writes.Count;
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        Assert.Equal(writeCount, context.Storage.Writes.Count);

        window.Close();
    }

    [AvaloniaFact]
    public async Task 菜单打开取消保留旧错误且保存失败继续使用唯一错误条()
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        var items = window.GetLogicalDescendants().OfType<MenuItem>().ToArray();
        var open = Assert.IsType<WorkbenchPresentationCommand>(
            Assert.Single(items, item => Equals(item.Header, "打开")).Command);
        var save = Assert.IsType<WorkbenchPresentationCommand>(
            Assert.Single(items, item => Equals(item.Header, "保存 (ctrl+s)")).Command);
        var operationState = context.Provider.GetRequiredService<DocumentOperationState>();
        operationState.Apply(DocumentOperationResult.Failure("已有错误"));

        var openResult = await open.ExecuteAsync();
        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, openResult.Status);
        Assert.Equal("已有错误", operationState.Error);

        var document = await CreateDocumentAsync(context);
        GetDocumentDock(context).ActiveDockable = document;
        var model = Assert.IsType<G4UiPersistableDocument>(document.Model);
        model.Edit("保存失败内容");
        context.Storage.SavePath = Path.Combine(context.TempDirectory, "failed-save.mamdoc");
        context.Storage.WriteException = new IOException("secret-ui-write-path");

        var saveResult = await save.ExecuteAsync();
        var banner = window.GetLogicalDescendants()
            .OfType<MainView>()
            .Single()
            .FindControl<Border>("DocumentOperationErrorBanner")!;

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, saveResult.Status);
        Assert.True(banner.IsVisible);
        Assert.True(operationState.HasError);
        Assert.DoesNotContain("secret-ui-write-path", operationState.Error, StringComparison.Ordinal);
        context.ViewModel.DismissDocumentOperationErrorCommand.Execute(null);
        Assert.False(banner.IsVisible);

        model.MarkCleanForCleanup();
        window.Close();
    }

    [AvaloniaFact]
    public async Task 工作线程状态变化切回UI线程且释放后不再刷新()
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        var save = Assert.IsType<WorkbenchPresentationCommand>(context.ViewModel.WorkbenchCommands.Save);
        var states = context.Provider
            .GetRequiredService<Business.Commands.State.WorkbenchCommandStateQuery>();
        var route = states.Resolve(HostWorkbenchCommandIds.SaveDocument);
        var refreshed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        save.CanExecuteChanged += (_, _) =>
            refreshed.TrySetResult(Dispatcher.UIThread.CheckAccess());

        await Task.Run(() => states.NotifyExecuted(route));
        Dispatcher.UIThread.RunJobs();
        Assert.True(await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(save.CanExecute(null));

        // 先从工作线程排入一次刷新，再在 UI 队列执行前释放独立适配器；迟到回调必须静默失效。
        var lateCommand = new WorkbenchPresentationCommand(
            HostWorkbenchCommandIds.SaveDocument,
            states,
            context.Provider.GetRequiredService<WorkbenchCommandExecutor>(),
            Dispatcher.UIThread);
        var lateRefreshes = 0;
        lateCommand.CanExecuteChanged += (_, _) => lateRefreshes++;
        await Task.Run(() =>
        {
            states.NotifyExecuted(route);
            lateCommand.Dispose();
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, lateRefreshes);

        window.Close();
    }

    [AvaloniaFact]
    public async Task 定向非相关全量通知正确且异常观察者不阻断后续刷新()
    {
        var diagnostics = new G4UiDiagnosticSink();
        using var context = CreateContext(diagnostics);
        var open = Assert.IsType<WorkbenchPresentationCommand>(
            context.ViewModel.WorkbenchCommands.Open);
        var save = Assert.IsType<WorkbenchPresentationCommand>(
            context.ViewModel.WorkbenchCommands.Save);
        var states = context.Provider
            .GetRequiredService<Business.Commands.State.WorkbenchCommandStateQuery>();
        var openRefreshes = 0;
        var saveRefreshes = 0;
        var survivingObserverRefreshes = 0;
        var survivingPropertyRefreshes = 0;
        open.CanExecuteChanged += (_, _) => openRefreshes++;
        save.CanExecuteChanged += (_, _) =>
            throw new InvalidOperationException("secret-observer-detail");
        save.CanExecuteChanged += (_, _) =>
        {
            saveRefreshes++;
            survivingObserverRefreshes++;
        };
        save.PropertyChanged += (_, _) =>
            throw new InvalidOperationException("secret-property-observer-detail");
        save.PropertyChanged += (_, args) =>
        {
            Assert.Equal(nameof(WorkbenchPresentationCommand.IsEnabled), args.PropertyName);
            survivingPropertyRefreshes++;
        };

        // Open 的定向通知对 Save 是非相关通知，不能让 Save 的菜单投影刷新。
        states.NotifyExecuted(states.Resolve(HostWorkbenchCommandIds.OpenDocument));
        Assert.Equal(1, openRefreshes);
        Assert.Equal(0, saveRefreshes);

        // Save 的定向通知即使遇到异常观察者，也必须继续调用后续观察者并记录脱敏诊断。
        states.NotifyExecuted(states.Resolve(HostWorkbenchCommandIds.SaveDocument));
        Assert.Equal(1, openRefreshes);
        Assert.Equal(1, saveRefreshes);
        Assert.Equal(1, survivingObserverRefreshes);
        Assert.Equal(1, survivingPropertyRefreshes);
        Assert.Collection(
            diagnostics.Drafts,
            diagnostic => AssertPresentationDiagnostic(diagnostic),
            diagnostic => AssertPresentationDiagnostic(diagnostic));

        // 活动目标变化是全量失效，Open 与 Save 都必须重新查询；保存状态随目标立即变为可用。
        var openBeforeFullRefresh = openRefreshes;
        var saveBeforeFullRefresh = saveRefreshes;
        var document = await CreateDocumentAsync(context);
        GetDocumentDock(context).ActiveDockable = document;
        Assert.True(openRefreshes > openBeforeFullRefresh);
        Assert.True(saveRefreshes > saveBeforeFullRefresh);
        Assert.True(save.CanExecute(null));

        Assert.IsType<G4UiPersistableDocument>(document.Model).MarkCleanForCleanup();
    }

    private static void AssertPresentationDiagnostic(HostDiagnosticDraft diagnostic)
    {
        Assert.Equal(HostDiagnosticCodes.WorkbenchCommandExecutionFailed, diagnostic.Code);
        Assert.Equal(HostWorkbenchCommandIds.SaveDocument.Value, diagnostic.StableId);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);
    }

    private static UiTestContext CreateContext(G4UiDiagnosticSink? diagnostics = null) =>
        new((services, builder) =>
    {
        if (diagnostics is not null)
        {
            services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        }
        services.AddScoped<G4UiPersistableDocument>();
        services.AddScoped<G4UiNonPersistableDocument>();
        services.AddSingleton<IHostDockableFactory>(provider =>
            new G4UiDockableFactory(
                provider.GetRequiredService<PluginRegistry>(),
                provider.GetRequiredService<DocumentScopeManager>(),
                provider.GetRequiredService<ViewLocator>(),
                provider));
        builder.AddDocument(
            Owner,
            new DocumentDescriptor(
                DocumentType,
                "G4 UI 文档",
                "验证 Host 保存 Presentation",
                "测试"),
            typeof(G4UiPersistableDocument),
            typeof(UserControl),
            static () => new UserControl(),
            isPersistable: true);
        builder.AddDocument(
            Owner,
            new DocumentDescriptor(
                NonPersistableDocumentType,
                "G4 UI 不可持久化文档",
                "验证 Host 保存 Presentation 的禁用状态",
                "测试"),
            typeof(G4UiNonPersistableDocument),
            typeof(UserControl),
            static () => new UserControl(),
            isPersistable: false);
    });

    /// <summary>记录 Presentation 边界产生的稳定诊断草稿，避免测试读取用户目录 JSONL。</summary>
    private sealed class G4UiDiagnosticSink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];

        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            Drafts.Add(draft);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty,
                Sequence = Drafts.Count,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = draft.Code,
                Severity = HostDiagnosticSeverity.Error,
                Phase = draft.Phase,
                Disposition = HostDiagnosticDisposition.Continue,
                StableId = draft.StableId,
                UserMessage = "G4 Headless 测试诊断",
            };
        }
    }

    private static async Task<ManagedDocumentDockable> CreateDocumentAsync(
        UiTestContext context,
        DocumentTypeId? documentTypeId = null)
    {
        var requestedDocumentTypeId = documentTypeId ?? DocumentType;
        var result = await context.Provider
            .GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(requestedDocumentTypeId);
        context.Provider.GetRequiredService<DocumentOperationState>().Apply(result);
        return GetDocumentDock(context).VisibleDockables!
            .OfType<ManagedDocumentDockable>()
            .Single(item =>
                item.Registration.Descriptor.DocumentTypeId == requestedDocumentTypeId);
    }

    private static DocumentDock GetDocumentDock(UiTestContext context) =>
        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<IDocumentDock>(
            Business.Layout.DockLayoutIds.Documents));

    /// <summary>
    /// 让本专项的虚拟插件 Document 使用测试容器 Scope，同时继续创建真实 Host Dock Adapter。
    /// </summary>
    /// <remarks>
    /// 普通生产插件由 <c>PluginProviderOwner</c> 持有私有 Provider；本专项没有伪造插件 ZIP 或第二个
    /// Provider，只需要验证 Host Presentation，因此用此窄工厂把唯一测试 Document 限制在测试容器。
    /// </remarks>
    private sealed class G4UiDockableFactory(
        PluginRegistry registry,
        DocumentScopeManager documentScopes,
        ViewLocator viewLocator,
        IServiceProvider provider) : IHostDockableFactory
    {
        public Document CreateHostDocument(
            DocumentTypeId documentTypeId,
            NewDocumentActivation activation)
        {
            var hostCatalog = provider.GetRequiredService<HostWorkspaceCatalog>();
            if (!hostCatalog.TryGetDocument(documentTypeId, out var registration))
            {
                throw new NotSupportedException(
                    $"不支持的 Host Document 类型：{documentTypeId.Value}。");
            }
            var lease = registration.ModelFactory();
            ManagedDocumentDockable? adapter = null;
            try
            {
                registration.Initialize(lease.Model, activation, lease.ClosingToken);
                adapter = new ManagedDocumentDockable(
                    new ActivatedWorkspaceDocument(registration, lease),
                    activation.Title);
                viewLocator.Prepare(adapter);
                return adapter;
            }
            catch
            {
                if (adapter is not null)
                {
                    adapter.Dispose();
                }
                else
                {
                    lease.Dispose();
                }
                throw;
            }
        }

        public async ValueTask<Document> CreateDocumentAsync(
            DocumentTypeId documentTypeId,
            DocumentActivation context)
        {
            if (!registry.TryGetDocumentRegistration(documentTypeId, out var registration))
            {
                throw new NotSupportedException(
                    $"不支持的测试 Document 类型：{documentTypeId.Value}。");
            }
            var lease = documentScopes.CreateDocument(registration.ModelType);
            ManagedDocumentDockable? adapter = null;
            try
            {
                await ((IPluginDocument)lease.Model)
                    .InitializeAsync(context, lease.ClosingToken);
                adapter = new ManagedDocumentDockable(
                    new ActivatedWorkspaceDocument(registration, lease),
                    context.Title);
                viewLocator.Prepare(adapter);
                return adapter;
            }
            catch
            {
                if (adapter is not null)
                {
                    adapter.Dispose();
                }
                else
                {
                    lease.Dispose();
                }
                throw;
            }
        }

        public Tool CreateTool(ToolTypeId toolTypeId)
        {
            var hostCatalog = provider.GetRequiredService<HostWorkspaceCatalog>();
            if (!hostCatalog.TryGetTool(toolTypeId, out var registration))
            {
                throw new NotSupportedException(
                    $"不支持的 Host Tool 类型：{toolTypeId.Value}。");
            }
            var adapter = new ManagedToolDockable(new ActivatedWorkspaceTool(
                registration,
                registration.ModelFactory()));
            try
            {
                viewLocator.Prepare(adapter);
                return adapter;
            }
            catch
            {
                adapter.Dispose();
                throw;
            }
        }
    }

    /// <summary>验证 Save 必须尊重注册元数据，而不能仅凭存在活动 Document 就启用。</summary>
    internal sealed class G4UiNonPersistableDocument : IPluginDocument
    {
        public DocumentPresentationState Presentation =>
            new("G4 UI 不可持久化文档");

        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }

        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Headless UI 保存闭环使用的最小修订化 Document。</summary>
    internal sealed class G4UiPersistableDocument : IPersistablePluginDocument
    {
        private long _revision;
        private long _acceptedRevision;
        private string _content = "initial";

        /// <summary>供真实 Document Scope 通过 Microsoft DI 创建测试实例。</summary>
        public G4UiPersistableDocument()
        {
        }

        public bool IsDirty => _revision != _acceptedRevision;

        public event EventHandler? IsDirtyChanged;

        public DocumentPresentationState Presentation => new("G4 UI 文档");

        public event EventHandler? PresentationChanged
        {
            add { }
            remove { }
        }

        public ValueTask InitializeAsync(
            DocumentActivation context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(_content));
            return ValueTask.FromResult(new DocumentSaveSnapshot(
                new DocumentRevision(_revision),
                new DocumentContent(1, json.RootElement)));
        }

        public void AcceptChanges(DocumentRevision savedRevision)
        {
            if (savedRevision.Value != _revision)
            {
                return;
            }
            var wasDirty = IsDirty;
            _acceptedRevision = _revision;
            if (wasDirty)
            {
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void Edit(string content)
        {
            var wasDirty = IsDirty;
            _content = content;
            _revision = checked(_revision + 1);
            if (wasDirty != IsDirty)
            {
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>只用于在保存失败断言后允许 Headless Window 完成同步关闭。</summary>
        internal void MarkCleanForCleanup()
        {
            var wasDirty = IsDirty;
            _acceptedRevision = _revision;
            if (wasDirty)
            {
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
