using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Recycling;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.Views.Welcome;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 使用生产 Dock 主题验证 Document 在四向分割期间不会把正文 View 发布到标签辅助 Presenter。
/// </summary>
public sealed class DockSplitVisualRegressionTests
{
    private static readonly HashSet<string?> AuxiliaryPresenterNames =
    [
        "PART_IconPresenter",
        "PART_CompactClosePresenter",
        "PART_HeaderPresenter",
        "PART_ModifiedPresenter",
        "PART_ClosePresenter"
    ];

    [AvaloniaFact]
    public async Task 四向分割均保持正文View单一父级且关闭后释放()
    {
        foreach (var operation in new[]
                 {
                     DockOperation.Top,
                     DockOperation.Bottom,
                     DockOperation.Left,
                     DockOperation.Right
                 })
        {
            await AssertSplitAsync(operation);
        }
    }

    private static async Task AssertSplitAsync(DockOperation operation)
    {
        using var context = new UiTestContext();
        await context.Workspace.CreateAndPublishDocumentAsync(
            HostExtensionIds.WelcomeDocument,
            new NewDocumentActivation($"欢迎-{operation}-A"));
        await context.Workspace.CreateAndPublishDocumentAsync(
            HostExtensionIds.WelcomeDocument,
            new NewDocumentActivation($"欢迎-{operation}-B"));

        var window = new MainWindow
        {
            Width = 1400,
            Height = 900,
            DataContext = context.ViewModel
        };
        var dockControl = window.GetLogicalDescendants()
            .OfType<DockControl>()
            .Single();
        var recycling = context.Provider.GetRequiredService<DocumentControlRecycling>();
        ControlRecyclingDataTemplate.SetControlRecycling(dockControl, recycling);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var root = Assert.IsAssignableFrom<IRootDock>(context.ViewModel.Layout);
            var documents = context.Workspace.GetDocuments();
            Assert.Equal(3, documents.Count);
            var source = documents[^1];
            var sourceDock = Assert.IsAssignableFrom<IDocumentDock>(source.Owner);
            var prepared = Assert.IsType<WelcomeView>(source.PreparedView);

            Assert.True(new DockService().SplitDockable(
                source,
                sourceDock,
                sourceDock,
                operation,
                bExecute: true));
            Dispatcher.UIThread.RunJobs();

            var destination = Assert.IsAssignableFrom<IDocumentDock>(source.Owner);
            Assert.NotSame(sourceDock, destination);
            Assert.Contains(source, destination.VisibleDockables ?? []);
            Assert.True(DockTreeNavigator.IsDockableAttached(root, source));
            Assert.Same(source.Model, prepared.DataContext);
            Assert.NotNull(prepared.GetVisualParent());
            Assert.DoesNotContain(
                prepared.GetVisualAncestors().OfType<ContentPresenter>(),
                presenter => AuxiliaryPresenterNames.Contains(presenter.Name));

            foreach (var presenter in window.GetVisualDescendants()
                         .OfType<ContentPresenter>()
                         .Where(item => AuxiliaryPresenterNames.Contains(item.Name)))
            {
                Assert.DoesNotContain(
                    presenter.GetVisualDescendants(),
                    visual => ReferenceEquals(visual, prepared));
            }

            context.Workspace.DockFactory.CloseDockable(source);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(source, context.Workspace.GetDocuments());
            Assert.Null(prepared.DataContext);
            Assert.Null(prepared.GetVisualParent());
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
