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
    public void 视图可完成绑定并提供只读可复制输出框()
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
                Equals(button.Content, "生成全部地址"));
            Assert.Contains(controls.OfType<TextBox>(), textBox =>
                textBox.IsReadOnly && textBox.AcceptsReturn);
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
        public Task<string?> PickWorkbookAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
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
