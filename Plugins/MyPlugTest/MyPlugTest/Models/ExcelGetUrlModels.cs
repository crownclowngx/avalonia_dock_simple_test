using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MyPlugTest.Models;

public sealed record ExcelColumnOption(int Index, string Name);

public sealed record ExcelPreviewCell(string Value);

public sealed record ExcelPreviewRow(int RowNumber, IReadOnlyList<ExcelPreviewCell> Cells);

public sealed record ExcelWorksheetPreview(
    string Name,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<ExcelColumnOption> Columns,
    IReadOnlyList<ExcelPreviewRow> Rows);

public sealed record ExcelWorkbookPreview(IReadOnlyList<ExcelWorksheetPreview> Worksheets);

public sealed record ExcelRowData(int RowNumber, IReadOnlyDictionary<int, string> Values)
{
    public string GetValue(int columnIndex) =>
        Values.TryGetValue(columnIndex, out var value) ? value : string.Empty;
}

public sealed record ExcelParameterMapping(string ParameterName, int ColumnIndex, string ColumnName);

public sealed record ExcelUrlBuildResult(
    IReadOnlyList<string> Urls,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

public sealed class ExcelParameterMappingViewModel : ObservableObject
{
    private readonly Action<ExcelParameterMappingViewModel> _remove;
    private readonly Action _changed;
    private string _parameterName = string.Empty;
    private ExcelColumnOption? _selectedColumn;
    private bool _canRemove;

    public ExcelParameterMappingViewModel(
        Action<ExcelParameterMappingViewModel> remove,
        Action changed)
    {
        _remove = remove;
        _changed = changed;
        RemoveCommand = new RelayCommand(() => _remove(this), () => CanRemove);
    }

    public ObservableCollection<ExcelColumnOption> AvailableColumns { get; } = [];

    public string ParameterName
    {
        get => _parameterName;
        set
        {
            if (SetProperty(ref _parameterName, value)) _changed();
        }
    }

    public ExcelColumnOption? SelectedColumn
    {
        get => _selectedColumn;
        set
        {
            if (SetProperty(ref _selectedColumn, value)) _changed();
        }
    }

    public bool CanRemove
    {
        get => _canRemove;
        set
        {
            if (!SetProperty(ref _canRemove, value)) return;
            RemoveCommand.NotifyCanExecuteChanged();
        }
    }

    public IRelayCommand RemoveCommand { get; }

    public void ReplaceColumns(IReadOnlyList<ExcelColumnOption> columns)
    {
        var selectedIndex = SelectedColumn?.Index;
        AvailableColumns.Clear();
        foreach (var column in columns) AvailableColumns.Add(column);
        SelectedColumn = selectedIndex is null
            ? null
            : AvailableColumns.FirstOrDefault(column => column.Index == selectedIndex);
    }
}
