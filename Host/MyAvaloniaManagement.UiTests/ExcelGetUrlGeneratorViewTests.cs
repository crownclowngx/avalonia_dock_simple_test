using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using MyPlugTest.Models;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;
using MyPlugTest.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class ExcelGetUrlGeneratorViewTests
{
    [AvaloniaFact]
    public void 视图可完成绑定并提供示例列表与TXT输出入口()
    {
        var viewModel = new ExcelGetUrlGeneratorViewModel(
            new EmptyDialogService(),
            new EmptyWorkbookReader(),
            new ExcelGetUrlBuilder());
        var view = new ExcelGetUrlGeneratorView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        try
        {
            var controls = view.GetLogicalDescendants().ToArray();
            Assert.Contains(controls.OfType<Button>(), button =>
                Equals(button.Content, "选择 Excel…"));
            Assert.Contains(controls.OfType<Button>(), button =>
                Equals(button.Content, "生成全部地址到 TXT"));
            Assert.Contains(controls.OfType<ItemsControl>(), itemsControl =>
                ReferenceEquals(itemsControl.ItemsSource, viewModel.ExampleUrls));
            Assert.Contains(controls.OfType<CheckBox>(), checkBox =>
                checkBox.IsChecked == true);
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    private sealed class EmptyDialogService : IExcelFileDialogService
    {
        /*
         * 设计意图：该 Headless 测试只验证生产 XAML 和绑定，不应打开原生文件窗口或访问
         * 文件系统。因此两个选择操作都以 null 表示用户取消；同时主动传播取消令牌，
         * 使这个最小 Stub 与生产实现保持一致的可观察取消行为。
         */
        public Task<string?> PickWorkbookAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickOutputTextFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class EmptyWorkbookReader : IExcelWorkbookReader
    {
        public Task<ExcelWorkbookPreview> ReadPreviewAsync(
            string filePath,
            int previewRowCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExcelWorkbookPreview([]));

        public Task<IReadOnlyList<ExcelRowData>> ReadRowsAsync(
            string filePath,
            string worksheetName,
            IReadOnlyCollection<int> columnIndexes,
            int startRow,
            int? maximumRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExcelRowData>>([]);
    }
}
