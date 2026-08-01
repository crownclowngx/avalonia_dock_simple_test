using System.Xml.Linq;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Views.BankBalanceReconciliation;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationPresentationTests
{
    [Fact]
    public void ViewLocator命名约定可以映射顶层和三个子View()
    {
        AssertMapping(typeof(BankBalanceReconciliationViewModel), typeof(BankBalanceReconciliationView));
        AssertMapping(typeof(ReconciliationSourceViewModel), typeof(ReconciliationSourceView));
        AssertMapping(typeof(ReconciliationOptionsViewModel), typeof(ReconciliationOptionsView));
        AssertMapping(typeof(ReconciliationRunViewModel), typeof(ReconciliationRunView));
    }

    [Fact]
    public void 顶层View仅组合子View且不设置运行时DataContext()
    {
        var path = FindRepositoryFile(
            "Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug",
            "Views", "BankBalanceReconciliation", "BankBalanceReconciliationView.axaml");
        var document = XDocument.Load(path);
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Null(root.Attribute("DataContext"));
        var children = root.Descendants()
            .Where(element => element.Name.LocalName is
                "ReconciliationSourceView" or "ReconciliationOptionsView" or "ReconciliationRunView")
            .ToArray();
        Assert.Equal(3, children.Length);
        Assert.All(children, child =>
            Assert.StartsWith("{Binding ", (string?)child.Attribute("DataContext"), StringComparison.Ordinal));
    }

    [Fact]
    public void 代码隐藏只负责初始化组件且页面禁用水平滚动()
    {
        var viewDirectory = Path.GetDirectoryName(FindRepositoryFile(
            "Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug",
            "Views", "BankBalanceReconciliation", "BankBalanceReconciliationView.axaml"))!;
        foreach (var codeBehind in Directory.GetFiles(viewDirectory, "*.axaml.cs"))
        {
            var text = File.ReadAllText(codeBehind);
            Assert.Contains("InitializeComponent();", text);
            Assert.DoesNotContain("DataContext =", text);
        }

        var rootXaml = File.ReadAllText(Path.Combine(viewDirectory, "BankBalanceReconciliationView.axaml"));
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", rootXaml);
    }

    [Fact]
    public void 账户选择器在同一行展示单位银行和银行账来源()
    {
        var path = FindRepositoryFile(
            "Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug",
            "Views", "BankBalanceReconciliation", "ReconciliationSourceView.axaml");
        var document = XDocument.Load(path);
        var comboBox = Assert.Single(document.Descendants(),
            element => element.Name.LocalName == "ComboBox" &&
                       (string?)element.Attribute("ItemsSource") == "{Binding Profiles}");
        Assert.Contains("recon-profile-picker", (string?)comboBox.Attribute("Classes"));

        var itemTemplate = Assert.Single(comboBox.Descendants(),
            element => element.Name.LocalName == "ComboBox.ItemTemplate");
        var row = Assert.Single(itemTemplate.Descendants(),
            element => element.Name.LocalName == "Grid");
        Assert.Equal("120,90,*", (string?)row.Attribute("ColumnDefinitions"));

        var displayedBindings = itemTemplate.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .ToArray();
        Assert.Contains("{Binding UnitShortName}", displayedBindings);
        Assert.Contains("{Binding BankShortName}", displayedBindings);
        Assert.Contains("{Binding SourceName}", displayedBindings);
    }

    private static void AssertMapping(Type viewModel, Type view)
    {
        var expected = viewModel.FullName!.Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        Assert.Equal(expected, view.FullName);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"找不到仓库文件：{Path.Combine(segments)}");
    }
}
