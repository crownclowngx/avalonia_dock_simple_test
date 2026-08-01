using System.Xml.Linq;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Views.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
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

    [Fact]
    public void 运行视图分别展示复核组和歧义并支持展开原始明细()
    {
        var path = FindRepositoryFile(
            "Plugins", "DaTangAccountingHelpPlug", "DaTangAccountingHelpPlug",
            "Views", "BankBalanceReconciliation", "ReconciliationRunView.axaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("StringFormat='复核组：{0}'", xaml);
        Assert.Contains("StringFormat='歧义：{0}'", xaml);
        Assert.Contains("x:DataType=\"vm:ReconciliationIssueViewModel\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding Entries}\"", xaml);
    }

    [Fact]
    public void 金额不等的凭证组压缩为一条复核标题并保留全部来源行()
    {
        var bank1 = ReconciliationTestData.Entry("B1", ReconciliationDirection.BankPaid, 3000m, "咨询费683", 100);
        var bank2 = ReconciliationTestData.Entry("B2", ReconciliationDirection.BankPaid, 3000m, "咨询费683", 101);
        var enterprise = ReconciliationTestData.Entry("E", ReconciliationDirection.EnterprisePaid, 6200m, "咨询费", 20);
        var decisions = new[] { bank1, bank2, enterprise }.Select(entry => new MatchDecision
        {
            Status = MatchDecisionStatus.Unmatched,
            PrimaryEntry = entry,
            RuleId = "reference-group-amount-mismatch",
            Reason = "金额不等",
            GroupKey = "reference:test:683",
            GroupTitle = "咨询费683",
            GroupEntryCount = 2
        });

        var issue = Assert.Single(ReconciliationIssueViewModel.Create(decisions));

        Assert.Equal("咨询费683", issue.Title);
        Assert.Equal("银行 2 笔 6,000.00｜企业 6,200.00｜差额 -200.00", issue.Summary);
        Assert.Equal(3, issue.EntryCount);
        Assert.Equal([100, 101, 20], issue.Entries.Select(entry => entry.SourceRow).ToArray());
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
