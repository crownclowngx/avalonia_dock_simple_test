using MyPlugTest.Models;
using OfficeOpenXml;

namespace MyPlugTest.Services;

public interface IExcelWorkbookReader
{
    Task<ExcelWorkbookPreview> ReadPreviewAsync(
        string filePath,
        int previewRowCount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExcelRowData>> ReadRowsAsync(
        string filePath,
        string worksheetName,
        IReadOnlyCollection<int> columnIndexes,
        int startRow,
        int? maximumRows,
        CancellationToken cancellationToken = default);
}

public sealed class EpplusExcelWorkbookReader : IExcelWorkbookReader
{
    public Task<ExcelWorkbookPreview> ReadPreviewAsync(
        string filePath,
        int previewRowCount,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ReadPreview(filePath, previewRowCount, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<ExcelRowData>> ReadRowsAsync(
        string filePath,
        string worksheetName,
        IReadOnlyCollection<int> columnIndexes,
        int startRow,
        int? maximumRows,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ReadRows(
                filePath,
                worksheetName,
                columnIndexes,
                startRow,
                maximumRows,
                cancellationToken),
            cancellationToken);

    private static ExcelWorkbookPreview ReadPreview(
        string filePath,
        int previewRowCount,
        CancellationToken cancellationToken)
    {
        ValidateFile(filePath);
        if (previewRowCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewRowCount));

        SetLicense();
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheets = new List<ExcelWorksheetPreview>();
        foreach (var worksheet in package.Workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dimension = worksheet.DimensionByValue;
            var endRow = dimension?.End.Row ?? 0;
            var endColumn = dimension?.End.Column ?? 0;
            var columns = Enumerable.Range(1, endColumn)
                .Select(index => new ExcelColumnOption(index, ExcelCellAddress.GetColumnLetter(index)))
                .ToArray();
            var rows = new List<ExcelPreviewRow>();
            for (var row = 1; row <= Math.Min(endRow, previewRowCount); row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new ExcelPreviewRow(
                    row,
                    Enumerable.Range(1, endColumn)
                        .Select(column => new ExcelPreviewCell(worksheet.Cells[row, column].Text))
                        .ToArray()));
            }

            worksheets.Add(new ExcelWorksheetPreview(
                worksheet.Name,
                endRow,
                endColumn,
                columns,
                rows));
        }

        if (worksheets.Count == 0)
            throw new InvalidDataException("Excel 工作簿中没有可用的工作表。");
        return new ExcelWorkbookPreview(worksheets);
    }

    private static IReadOnlyList<ExcelRowData> ReadRows(
        string filePath,
        string worksheetName,
        IReadOnlyCollection<int> columnIndexes,
        int startRow,
        int? maximumRows,
        CancellationToken cancellationToken)
    {
        ValidateFile(filePath);
        if (string.IsNullOrWhiteSpace(worksheetName))
            throw new ArgumentException("必须选择工作表。", nameof(worksheetName));
        if (columnIndexes.Count == 0)
            throw new ArgumentException("必须选择至少一列。", nameof(columnIndexes));
        if (columnIndexes.Any(index => index <= 0))
            throw new ArgumentOutOfRangeException(nameof(columnIndexes));
        if (startRow <= 0)
            throw new ArgumentOutOfRangeException(nameof(startRow));
        if (maximumRows is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));

        SetLicense();
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = package.Workbook.Worksheets[worksheetName]
                        ?? throw new InvalidDataException($"找不到工作表“{worksheetName}”。");
        var dimension = worksheet.DimensionByValue;
        if (dimension is null || startRow > dimension.End.Row) return [];

        var columns = columnIndexes.Distinct().Order().ToArray();
        var endRow = FindLastMappedRow(worksheet, columns, startRow, dimension.End.Row, cancellationToken);
        if (endRow < startRow) return [];

        var rows = new List<ExcelRowData>();
        for (var row = startRow; row <= endRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = columns.ToDictionary(column => column, column => worksheet.Cells[row, column].Text);
            if (values.Values.All(string.IsNullOrEmpty)) continue;
            rows.Add(new ExcelRowData(row, values));
            if (maximumRows is not null && rows.Count >= maximumRows.Value) break;
        }

        return rows;
    }

    private static int FindLastMappedRow(
        ExcelWorksheet worksheet,
        IReadOnlyList<int> columns,
        int startRow,
        int endRow,
        CancellationToken cancellationToken)
    {
        for (var row = endRow; row >= startRow; row--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (columns.Any(column => !string.IsNullOrEmpty(worksheet.Cells[row, column].Text)))
                return row;
        }

        return startRow - 1;
    }

    private static void ValidateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Excel 文件路径不能为空。", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Excel 文件不存在。", filePath);
        var extension = Path.GetExtension(filePath);
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("仅支持 .xlsx 和 .xlsm 工作簿。");
    }

    private static void SetLicense() =>
        ExcelPackage.License.SetNonCommercialPersonal("MyPlugTest");
}
