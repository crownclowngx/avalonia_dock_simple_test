using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Models;
using MyPlugTest.Services;

namespace MyPlugTest.ViewModels;

public sealed class ExcelGetUrlGeneratorViewModel : Document, IDisposable
{
    private const int PreviewRowCount = 5;
    private readonly IExcelFileDialogService _fileDialogService;
    private readonly IExcelWorkbookReader _workbookReader;
    private readonly ExcelGetUrlBuilder _urlBuilder;
    private readonly IDocumentLifetime? _documentLifetime;
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _exampleCts;
    private string _baseAddress = string.Empty;
    private string _workbookPath = string.Empty;
    private ExcelWorksheetPreview? _selectedWorksheet;
    private bool _skipHeader = true;
    private string _exampleMessage = "请选择 Excel 文件并配置参数映射。";
    private string _statusText = "等待选择 Excel 文件";
    private string _validationText = string.Empty;
    private string _outputFilePath = string.Empty;
    private bool _isOutputStale;
    private bool _isBusy;
    private bool _hasGeneratedResult;
    private bool _suppressInputChanged;
    private int _disposed;

    public ExcelGetUrlGeneratorViewModel(
        IExcelFileDialogService fileDialogService,
        IExcelWorkbookReader workbookReader,
        ExcelGetUrlBuilder urlBuilder,
        IDocumentLifetime? documentLifetime = null)
    {
        _fileDialogService = fileDialogService;
        _workbookReader = workbookReader;
        _urlBuilder = urlBuilder;
        _documentLifetime = documentLifetime;
        SelectWorkbookCommand = new AsyncRelayCommand(SelectWorkbookAsync);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync);
        AddMappingCommand = new RelayCommand(AddMapping);
        ParameterMappings.Add(CreateMapping());
        UpdateRemoveAvailability();
    }

    public ObservableCollection<ExcelWorksheetPreview> Worksheets { get; } = [];
    public ObservableCollection<ExcelColumnOption> PreviewColumns { get; } = [];
    public ObservableCollection<ExcelPreviewRow> PreviewRows { get; } = [];
    public ObservableCollection<ExcelParameterMappingViewModel> ParameterMappings { get; } = [];
    public ObservableCollection<string> ExampleUrls { get; } = [];

    public string BaseAddress
    {
        get => _baseAddress;
        set
        {
            if (!SetProperty(ref _baseAddress, value)) return;
            OnGeneratingInputChanged();
        }
    }

    public string WorkbookPath
    {
        get => _workbookPath;
        private set => SetProperty(ref _workbookPath, value);
    }

    public ExcelWorksheetPreview? SelectedWorksheet
    {
        get => _selectedWorksheet;
        set
        {
            if (!SetProperty(ref _selectedWorksheet, value)) return;
            ApplySelectedWorksheet();
            OnGeneratingInputChanged();
        }
    }

    public bool SkipHeader
    {
        get => _skipHeader;
        set
        {
            if (!SetProperty(ref _skipHeader, value)) return;
            OnGeneratingInputChanged();
        }
    }

    public string ExampleMessage
    {
        get => _exampleMessage;
        private set => SetProperty(ref _exampleMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    public string OutputFilePath
    {
        get => _outputFilePath;
        private set => SetProperty(ref _outputFilePath, value);
    }

    public bool IsOutputStale
    {
        get => _isOutputStale;
        private set => SetProperty(ref _isOutputStale, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanInteract));
        }
    }

    public bool CanInteract => !IsBusy;

    public IAsyncRelayCommand SelectWorkbookCommand { get; }
    public IAsyncRelayCommand GenerateCommand { get; }
    public IRelayCommand AddMappingCommand { get; }

    private async Task SelectWorkbookAsync(CancellationToken commandToken)
    {
        using var linked = CreateLinkedTokenSource(commandToken);
        var cancellationToken = linked.Token;
        try
        {
            var path = await _fileDialogService.PickWorkbookAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(path) || IsClosing) return;

            IsBusy = true;
            StatusText = "正在读取 Excel 工作簿…";
            ValidationText = string.Empty;
            var preview = await _workbookReader.ReadPreviewAsync(
                path,
                PreviewRowCount,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosing) return;

            _suppressInputChanged = true;
            try
            {
                WorkbookPath = path;
                Worksheets.Clear();
                foreach (var worksheet in preview.Worksheets) Worksheets.Add(worksheet);
                SelectedWorksheet = Worksheets.FirstOrDefault();
            }
            finally
            {
                _suppressInputChanged = false;
            }

            MarkOutputStale();
            StatusText = $"已载入 {Worksheets.Count} 个工作表";
            QueueExampleRefresh();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsClosing)
            {
                StatusText = "Excel 读取失败";
                ValidationText = exception.Message;
            }
        }
        finally
        {
            if (!IsClosing) IsBusy = false;
        }
    }

    private void ApplySelectedWorksheet()
    {
        var wasSuppressed = _suppressInputChanged;
        _suppressInputChanged = true;
        try
        {
            PreviewColumns.Clear();
            PreviewRows.Clear();
            if (SelectedWorksheet is not null)
            {
                foreach (var column in SelectedWorksheet.Columns) PreviewColumns.Add(column);
                foreach (var row in SelectedWorksheet.Rows) PreviewRows.Add(row);
            }

            foreach (var mapping in ParameterMappings)
                mapping.ReplaceColumns(PreviewColumns);
        }
        finally
        {
            _suppressInputChanged = wasSuppressed;
        }
    }

    private void AddMapping()
    {
        var mapping = CreateMapping();
        ParameterMappings.Add(mapping);
        var wasSuppressed = _suppressInputChanged;
        _suppressInputChanged = true;
        try
        {
            mapping.ReplaceColumns(PreviewColumns);
            mapping.SelectedColumn = FindFirstUnusedColumn();
        }
        finally
        {
            _suppressInputChanged = wasSuppressed;
        }
        UpdateRemoveAvailability();
        OnGeneratingInputChanged();
    }

    private void RemoveMapping(ExcelParameterMappingViewModel mapping)
    {
        if (ParameterMappings.Count <= 1 || !ParameterMappings.Remove(mapping)) return;
        UpdateRemoveAvailability();
        OnGeneratingInputChanged();
    }

    private ExcelParameterMappingViewModel CreateMapping() =>
        new(RemoveMapping, OnGeneratingInputChanged);

    private ExcelColumnOption? FindFirstUnusedColumn()
    {
        var used = ParameterMappings
            .Select(mapping => mapping.SelectedColumn?.Index)
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .ToHashSet();
        return PreviewColumns.FirstOrDefault(column => !used.Contains(column.Index))
               ?? PreviewColumns.FirstOrDefault();
    }

    private void UpdateRemoveAvailability()
    {
        var canRemove = ParameterMappings.Count > 1;
        foreach (var mapping in ParameterMappings) mapping.CanRemove = canRemove;
    }

    private void OnGeneratingInputChanged()
    {
        if (_suppressInputChanged || IsClosing) return;
        MarkOutputStale();
        QueueExampleRefresh();
    }

    private void MarkOutputStale()
    {
        if (!_hasGeneratedResult) return;
        IsOutputStale = true;
        StatusText = "输入已变化，当前结果已过期";
    }

    private void QueueExampleRefresh()
    {
        _exampleCts?.Cancel();
        _exampleCts?.Dispose();
        _exampleCts = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCts.Token,
            _documentLifetime?.ClosingToken ?? CancellationToken.None);
        _ = RefreshExamplesAsync(_exampleCts.Token);
    }

    private async Task RefreshExamplesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var worksheet = SelectedWorksheet;
            var mappings = SnapshotMappings();
            var configurationErrors = _urlBuilder.ValidateConfiguration(BaseAddress, mappings);
            if (WorkbookPath.Length == 0 || worksheet is null)
            {
                ReplaceExamples([], "请选择 Excel 文件并配置参数映射。");
                return;
            }
            if (configurationErrors.Count > 0)
            {
                ReplaceExamples([], string.Join(Environment.NewLine, configurationErrors));
                return;
            }

            var rows = await _workbookReader.ReadRowsAsync(
                WorkbookPath,
                worksheet.Name,
                mappings.Select(mapping => mapping.ColumnIndex).ToArray(),
                SkipHeader ? 2 : 1,
                PreviewRowCount,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = _urlBuilder.Build(BaseAddress, mappings, rows, worksheet.Name);
            if (result.IsSuccess)
                ReplaceExamples(result.Urls, result.Urls.Count == 0 ? "没有可生成示例的数据行。" : string.Empty);
            else
                ReplaceExamples([], string.Join(Environment.NewLine, result.Errors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsClosing) ReplaceExamples([], exception.Message);
        }
    }

    private void ReplaceExamples(IEnumerable<string> urls, string message)
    {
        if (IsClosing) return;
        ExampleUrls.Clear();
        foreach (var url in urls) ExampleUrls.Add(url);
        ExampleMessage = message;
    }

    private async Task GenerateAsync(CancellationToken commandToken)
    {
        using var linked = CreateLinkedTokenSource(commandToken);
        var cancellationToken = linked.Token;
        try
        {
            var worksheet = SelectedWorksheet;
            var mappings = SnapshotMappings();
            if (WorkbookPath.Length == 0 || worksheet is null)
            {
                StatusText = "无法生成";
                ValidationText = "请先选择 Excel 文件和工作表。";
                return;
            }

            var configurationErrors = _urlBuilder.ValidateConfiguration(BaseAddress, mappings);
            if (configurationErrors.Count > 0)
            {
                StatusText = "配置校验失败，旧结果未被覆盖";
                ValidationText = string.Join(Environment.NewLine, configurationErrors);
                return;
            }

            var suggestedFileName = CreateSuggestedOutputFileName(WorkbookPath, worksheet.Name);
            var outputPath = await _fileDialogService.PickOutputTextFileAsync(
                suggestedFileName,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(outputPath) || IsClosing)
            {
                StatusText = "已取消生成";
                return;
            }

            IsBusy = true;
            StatusText = "正在读取并校验全部数据…";
            ValidationText = string.Empty;
            var rows = await _workbookReader.ReadRowsAsync(
                WorkbookPath,
                worksheet.Name,
                mappings.Select(mapping => mapping.ColumnIndex).ToArray(),
                SkipHeader ? 2 : 1,
                null,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = _urlBuilder.Build(BaseAddress, mappings, rows, worksheet.Name);
            if (!result.IsSuccess)
            {
                StatusText = $"发现 {result.Errors.Count} 个问题，旧结果未被覆盖";
                ValidationText = string.Join(Environment.NewLine, result.Errors);
                return;
            }

            if (IsClosing) return;
            await File.WriteAllLinesAsync(
                outputPath,
                result.Urls,
                new UTF8Encoding(false),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosing) return;
            OutputFilePath = outputPath;
            _hasGeneratedResult = true;
            IsOutputStale = false;
            ValidationText = string.Empty;
            StatusText = $"生成完成：共 {result.Urls.Count} 个地址，已写入 TXT 文件";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsClosing)
            {
                StatusText = "生成失败，旧结果未被覆盖";
                ValidationText = exception.Message;
            }
        }
        finally
        {
            if (!IsClosing) IsBusy = false;
        }
    }

    private ExcelParameterMapping[] SnapshotMappings() =>
        ParameterMappings.Select(mapping => new ExcelParameterMapping(
            mapping.ParameterName,
            mapping.SelectedColumn?.Index ?? 0,
            mapping.SelectedColumn?.Name ?? string.Empty)).ToArray();

    private static string CreateSuggestedOutputFileName(string workbookPath, string worksheetName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeWorksheetName = new string(worksheetName
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        return $"{Path.GetFileNameWithoutExtension(workbookPath)}_{safeWorksheetName}_urls.txt";
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime?.ClosingToken ?? CancellationToken.None);

    private bool IsClosing =>
        Volatile.Read(ref _disposed) != 0 || _documentLifetime?.IsClosing == true;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SelectWorkbookCommand.Cancel();
        GenerateCommand.Cancel();
        _exampleCts?.Cancel();
        _exampleCts?.Dispose();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
