using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.ViewModels.Hello;

namespace MyAvaloniaManagement.Tests;

public sealed class WelcomePageTests
{
    [Fact]
    public void WelcomeCommandsRequestTheExpectedHostTools()
    {
        var requestedToolIds = new List<MyAvaloniaManagement.PluginSdk.ToolTypeId>();
        var viewModel = new WelcomeViewModel(requestedToolIds.Add);

        viewModel.OpenPluginMenuCommand.Execute(null);
        viewModel.OpenToolManagementCommand.Execute(null);

        Assert.Equal(
            [HostExtensionIds.PluginMenu, HostExtensionIds.ToolManagement],
            requestedToolIds);
    }

    [Fact]
    public void WelcomeContentHasAUsefulDefaultAndRuntimeVersion()
    {
        var viewModel = new WelcomeViewModel();

        Assert.Contains("Avalonia", viewModel.Text);
        Assert.StartsWith("版本 ", viewModel.VersionText);
        Assert.True(viewModel.VersionText.Length > "版本 ".Length);
    }

    [Fact]
    public void ShowToolActivatesVisibleHiddenAndPinnedTools()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var workspace = context.Workspace;
        var pluginMenu = Assert.IsAssignableFrom<Tool>(
            workspace.CreatedTools[HostExtensionIds.PluginMenu.Value]);

        Assert.True(workspace.ShowTool(HostExtensionIds.PluginMenu));
        Assert.Same(pluginMenu, Assert.IsAssignableFrom<IDock>(pluginMenu.Owner).ActiveDockable);

        workspace.DockFactory.HideDockable(pluginMenu);
        Assert.True(workspace.ShowTool(HostExtensionIds.PluginMenu));
        Assert.Same(pluginMenu, Assert.IsAssignableFrom<IDock>(pluginMenu.Owner).ActiveDockable);

        workspace.DockFactory.PinDockable(pluginMenu);
        var owningRoot = workspace.DockFactory.FindRoot(pluginMenu, _ => true)!;
        Assert.Contains(pluginMenu, owningRoot.RightPinnedDockables!);

        Assert.True(workspace.ShowTool(HostExtensionIds.PluginMenu));
        Assert.Contains(pluginMenu, owningRoot.RightPinnedDockables!);
    }

    [Fact]
    public void ShowToolRejectsUnknownStronglyTypedToolId()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();

        Assert.False(context.Workspace.ShowTool(
            new MyAvaloniaManagement.PluginSdk.ToolTypeId(
                "myavalonia.host.tool.missing")));
    }
}
