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
        var requestedToolIds = new List<string>();
        var viewModel = new WelcomeViewModel(requestedToolIds.Add);

        viewModel.OpenPluginMenuCommand.Execute(null);
        viewModel.OpenToolManagementCommand.Execute(null);

        Assert.Equal(
            [DockNameConstant.PlugGroupMenu, DockNameConstant.ToolManagement],
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
        var factory = context.Factory;
        var pluginMenu = Assert.IsAssignableFrom<Tool>(
            factory.CreatedTools[DockNameConstant.PlugGroupMenu]);

        Assert.True(factory.ShowTool(pluginMenu.Id));
        Assert.Same(pluginMenu, Assert.IsAssignableFrom<IDock>(pluginMenu.Owner).ActiveDockable);

        factory.HideDockable(pluginMenu);
        Assert.True(factory.ShowTool(pluginMenu.Id));
        Assert.Same(pluginMenu, Assert.IsAssignableFrom<IDock>(pluginMenu.Owner).ActiveDockable);

        factory.PinDockable(pluginMenu);
        var owningRoot = factory.FindRoot(pluginMenu, _ => true)!;
        Assert.Contains(pluginMenu, owningRoot.RightPinnedDockables!);

        Assert.True(factory.ShowTool(pluginMenu.Id));
        Assert.Contains(pluginMenu, owningRoot.RightPinnedDockables!);
    }

    [Fact]
    public void ShowToolRejectsUnknownToolId()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();

        Assert.False(context.Factory.ShowTool("missing-tool"));
        Assert.False(context.Factory.ShowTool(string.Empty));
    }
}
