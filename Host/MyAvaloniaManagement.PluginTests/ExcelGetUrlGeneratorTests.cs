using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Message;
using MyPlugTest.Create;
using MyPlugTest.Models;
using MyPlugTest.Plugin;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;
using OfficeOpenXml;

namespace MyAvaloniaManagement.PluginTests;

public sealed class ExcelGetUrlGeneratorTests
{
    [Fact]
    public void 中文与空参数按映射顺序拼接且保留已有查询串()
    {
        var builder = new ExcelGetUrlBuilder();
        var result = builder.Build(
            " https://api.test/items?fixed=1 ",
            [
                new ExcelParameterMapping("id", 1, "A"),
                new ExcelParameterMapping("name", 2, "B"),
                new ExcelParameterMapping("remark", 3, "C"),
            ],
            [
                new ExcelRowData(2, new Dictionary<int, string>
                {
                    [1] = "42",
                    [2] = "张三",
                    [3] = string.Empty,
                }),
            ],
            "数据");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["https://api.test/items?fixed=1&id=42&name=张三&remark="],
            result.Urls);
    }

    [Fact]
    public void 危险字符汇总准确坐标且整批不返回部分地址()
    {
        var builder = new ExcelGetUrlBuilder();
        var result = builder.Build(
            "https://api.test/items",
            [
                new ExcelParameterMapping("id", 1, "A"),
                new ExcelParameterMapping("name", 2, "B"),
            ],
            [
                new ExcelRowData(2, new Dictionary<int, string> { [1] = "ok", [2] = "张三" }),
                new ExcelRowData(4, new Dictionary<int, string> { [1] = "bad value", [2] = "李&四" }),
            ],
            "客户表");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Urls);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error =>
            error.Contains("第 4 行、A 列", StringComparison.Ordinal) &&
            error.Contains("空格", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error =>
            error.Contains("第 4 行、B 列", StringComparison.Ordinal) &&
            error.Contains("“&”", StringComparison.Ordinal));
    }

    [Fact]
    public void 配置拒绝非法地址重复参数与非法参数名()
    {
        var builder = new ExcelGetUrlBuilder();

        var fragmentErrors = builder.ValidateConfiguration(
            "https://api.test/items#section",
            [new ExcelParameterMapping("id", 1, "A")]);
        var duplicateErrors = builder.ValidateConfiguration(
            "https://api.test/items?ID=1",
            [new ExcelParameterMapping("id", 1, "A")]);
        var invalidNameErrors = builder.ValidateConfiguration(
            "ftp://api.test/items",
            [new ExcelParameterMapping("用户 id", 0, string.Empty)]);

        Assert.Contains(fragmentErrors, error => error.Contains("fragment", StringComparison.Ordinal));
        Assert.Contains(duplicateErrors, error => error.Contains("重复", StringComparison.Ordinal));
        Assert.Contains(invalidNameErrors, error => error.Contains("http/https", StringComparison.Ordinal));
        Assert.Contains(invalidNameErrors, error => error.Contains("参数名", StringComparison.Ordinal));
        Assert.Contains(invalidNameErrors, error => error.Contains("尚未选择", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://api.test/items", "https://api.test/items?id=1")]
    [InlineData("https://api.test/items?", "https://api.test/items?id=1")]
    [InlineData("https://api.test/items?fixed=2&", "https://api.test/items?fixed=2&id=1")]
    public void 根据基础地址结尾选择正确查询分隔符(string baseAddress, string expected)
    {
        var result = new ExcelGetUrlBuilder().Build(
            baseAddress,
            [new ExcelParameterMapping("id", 1, "A")],
            [new ExcelRowData(1, new Dictionary<int, string> { [1] = "1" })],
            "数据");

        Assert.True(result.IsSuccess);
        Assert.Equal([expected], result.Urls);
    }

    [Fact]
    public async Task EPPlus读取多工作表前五行与AA列并释放文件句柄()
    {
        var path = CreateWorkbook(package =>
        {
            var first = package.Workbook.Worksheets.Add("数据");
            first.Cells[1, 1].Value = "标题";
            first.Cells[2, 1].Value = "A2";
            first.Cells[2, 27].Value = "AA2";
            first.Cells[4, 27].Value = "AA4";
            first.Cells[6, 1].Value = "A6";
            package.Workbook.Worksheets.Add("第二页").Cells[1, 1].Value = "内容";
        });
        var macroEnabledPath = Path.ChangeExtension(path, ".xlsm");

        try
        {
            var reader = new EpplusExcelWorkbookReader();
            var preview = await reader.ReadPreviewAsync(path, 5);
            var sheet = Assert.Single(preview.Worksheets, worksheet => worksheet.Name == "数据");

            Assert.Equal(27, sheet.ColumnCount);
            Assert.Equal("AA", sheet.Columns[^1].Name);
            Assert.Equal([1, 2, 3, 4, 5], sheet.Rows.Select(row => row.RowNumber));

            var rows = await reader.ReadRowsAsync(path, "数据", [1, 27], 2, null);
            Assert.Equal([2, 4, 6], rows.Select(row => row.RowNumber));
            Assert.Equal("AA2", rows[0].GetValue(27));

            File.Copy(path, macroEnabledPath);
            var macroEnabledPreview = await reader.ReadPreviewAsync(macroEnabledPath, 5);
            Assert.Equal(["数据", "第二页"], macroEnabledPreview.Worksheets.Select(x => x.Name));
        }
        finally
        {
            File.Delete(path);
            File.Delete(macroEnabledPath);
        }

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(macroEnabledPath));
    }

    [Fact]
    public async Task 切换工作表保留有效列并清空越界列()
    {
        var reader = new SwitchingWorkbookReader();
        var viewModel = new ExcelGetUrlGeneratorViewModel(
            new StubExcelFileDialogService("input.xlsx"),
            reader,
            new ExcelGetUrlBuilder());

        await viewModel.SelectWorkbookCommand.ExecuteAsync(null);
        viewModel.ParameterMappings[0].SelectedColumn =
            viewModel.ParameterMappings[0].AvailableColumns.Single(column => column.Name == "B");
        viewModel.AddMappingCommand.Execute(null);
        viewModel.ParameterMappings[1].SelectedColumn =
            viewModel.ParameterMappings[1].AvailableColumns.Single(column => column.Name == "C");

        viewModel.SelectedWorksheet = viewModel.Worksheets.Single(sheet => sheet.Name == "窄表");

        Assert.Equal("B", viewModel.ParameterMappings[0].SelectedColumn?.Name);
        Assert.Null(viewModel.ParameterMappings[1].SelectedColumn);
        viewModel.Dispose();
    }

    [Fact]
    public async Task ViewModel成功后输入变化标记过期且失败不覆盖旧输出()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"excel-get-output-{Guid.NewGuid():N}.txt");
        var reader = new MutableWorkbookReader
        {
            Rows = [new ExcelRowData(2, new Dictionary<int, string> { [1] = "张三" })],
        };
        var viewModel = new ExcelGetUrlGeneratorViewModel(
            new StubExcelFileDialogService("input.xlsx", outputPath),
            reader,
            new ExcelGetUrlBuilder());

        try
        {
            await viewModel.SelectWorkbookCommand.ExecuteAsync(null);
            viewModel.BaseAddress = "https://api.test/users";
            viewModel.ParameterMappings[0].ParameterName = "name";
            viewModel.ParameterMappings[0].SelectedColumn =
                viewModel.ParameterMappings[0].AvailableColumns.Single();
            await viewModel.GenerateCommand.ExecuteAsync(null);

            Assert.Equal(outputPath, viewModel.OutputFilePath);
            var successfulOutput = await File.ReadAllLinesAsync(outputPath);
            Assert.Equal(["https://api.test/users?name=张三"], successfulOutput);
            Assert.False(viewModel.IsOutputStale);

            viewModel.BaseAddress = "https://api.test/changed";
            Assert.True(viewModel.IsOutputStale);
            reader.Rows = [new ExcelRowData(2, new Dictionary<int, string> { [1] = "bad value" })];
            await viewModel.GenerateCommand.ExecuteAsync(null);

            Assert.Equal(successfulOutput, await File.ReadAllLinesAsync(outputPath));
            Assert.Contains("第 2 行、A 列", viewModel.ValidationText);
            Assert.Contains("旧结果未被覆盖", viewModel.StatusText);
        }
        finally
        {
            // 设计意图：测试拥有自己创建的输出文件，因此无论断言或命令是否失败，
            // 都必须在这里释放 ViewModel 并清理文件，避免污染后续测试和开发者环境。
            viewModel.Dispose();
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void 新Document由独立Scope创建并注册为Scoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new MyPlugTestPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.my-plug-test"), services));

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(ExcelGetUrlGeneratorViewModel)).Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var strategy = ActivatorUtilities.CreateInstance<ExcelGetUrlGeneratorDocumentStrategy>(
            provider);
        var document = Assert.IsType<ExcelGetUrlGeneratorViewModel>(strategy.CreateDocument(
            new DocumentCreationParams(strategy.GetMetadata().DocumentTypeId)));

        Assert.Equal("Excel GET 地址生成器", document.Title);
        Assert.Equal(
            "myavalonia.plugin.my-plug-test.document.excel-get-url-generator",
            strategy.GetMetadata().DocumentTypeId.Value);
        Assert.True(provider.GetRequiredService<DocumentScopeManager>().Release(document));
    }

    private static string CreateWorkbook(Action<ExcelPackage> configure)
    {
        ExcelPackage.License.SetNonCommercialPersonal("MyAvaloniaManagement.PluginTests");
        var path = Path.Combine(Path.GetTempPath(), $"excel-get-{Guid.NewGuid():N}.xlsx");
        using var package = new ExcelPackage();
        configure(package);
        package.SaveAs(new FileInfo(path));
        return path;
    }

    /*
     * 设计意图：这是确定性的 Stub，只负责替代原生文件选择器，不模拟 Avalonia 窗口。
     * 输入工作簿与输出 TXT 使用两个独立路径，防止生成测试意外覆盖输入文件；未提供
     * 输出路径时返回 null，严格表达生产契约中的“用户取消保存”。即使 Stub 立即返回，
     * 也先传播取消令牌，保证它能够替换生产实现而不改变调用方可观察到的取消语义。
     */
    private sealed class StubExcelFileDialogService(
        string workbookPath,
        string? outputTextPath = null) : IExcelFileDialogService
    {
        public Task<string?> PickWorkbookAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(workbookPath);
        }

        public Task<string?> PickOutputTextFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(outputTextPath);
        }
    }

    private sealed class MutableWorkbookReader : IExcelWorkbookReader
    {
        public IReadOnlyList<ExcelRowData> Rows { get; set; } = [];

        public Task<ExcelWorkbookPreview> ReadPreviewAsync(
            string filePath,
            int previewRowCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExcelWorkbookPreview(
            [
                new ExcelWorksheetPreview(
                    "数据",
                    2,
                    1,
                    [new ExcelColumnOption(1, "A")],
                    [new ExcelPreviewRow(1, [new ExcelPreviewCell("标题")])]),
            ]));

        public Task<IReadOnlyList<ExcelRowData>> ReadRowsAsync(
            string filePath,
            string worksheetName,
            IReadOnlyCollection<int> columnIndexes,
            int startRow,
            int? maximumRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExcelRowData>>(
                maximumRows is null ? Rows : Rows.Take(maximumRows.Value).ToArray());
    }

    private sealed class SwitchingWorkbookReader : IExcelWorkbookReader
    {
        public Task<ExcelWorkbookPreview> ReadPreviewAsync(
            string filePath,
            int previewRowCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExcelWorkbookPreview(
            [
                Sheet("宽表", 3),
                Sheet("窄表", 2),
            ]));

        public Task<IReadOnlyList<ExcelRowData>> ReadRowsAsync(
            string filePath,
            string worksheetName,
            IReadOnlyCollection<int> columnIndexes,
            int startRow,
            int? maximumRows,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExcelRowData>>([]);

        private static ExcelWorksheetPreview Sheet(string name, int columnCount) =>
            new(
                name,
                1,
                columnCount,
                Enumerable.Range(1, columnCount)
                    .Select(index => new ExcelColumnOption(
                        index,
                        index switch { 1 => "A", 2 => "B", _ => "C" }))
                    .ToArray(),
                []);
    }
}
