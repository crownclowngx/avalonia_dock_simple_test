using System.Text;
using System.Text.RegularExpressions;
using MyPlugTest.Models;

namespace MyPlugTest.Services;

public sealed partial class ExcelGetUrlBuilder
{
    [GeneratedRegex("^[A-Za-z0-9._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNameRegex();

    public ExcelUrlBuildResult Build(
        string baseAddress,
        IReadOnlyList<ExcelParameterMapping> mappings,
        IReadOnlyList<ExcelRowData> rows,
        string worksheetName)
    {
        var errors = ValidateConfiguration(baseAddress, mappings, out var normalizedBaseAddress);
        if (errors.Count > 0) return new ExcelUrlBuildResult([], errors);

        var urls = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var rowHasError = false;
            foreach (var mapping in mappings)
            {
                var value = row.GetValue(mapping.ColumnIndex);
                var invalidCharacters = value.EnumerateRunes()
                    .Where(rune => !IsAllowedValueRune(rune))
                    .Select(DescribeRune)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (invalidCharacters.Length == 0) continue;

                rowHasError = true;
                errors.Add(
                    $"工作表“{worksheetName}”第 {row.RowNumber} 行、{mapping.ColumnName} 列" +
                    $"（参数 {mapping.ParameterName}）包含不允许的字符：{string.Join("、", invalidCharacters)}。");
            }

            if (rowHasError) continue;
            var separator = GetSeparator(normalizedBaseAddress);
            var query = string.Join(
                "&",
                mappings.Select(mapping =>
                    $"{mapping.ParameterName}={row.GetValue(mapping.ColumnIndex)}"));
            urls.Add(normalizedBaseAddress + separator + query);
        }

        return errors.Count == 0
            ? new ExcelUrlBuildResult(urls, [])
            : new ExcelUrlBuildResult([], errors);
    }

    public IReadOnlyList<string> ValidateConfiguration(
        string baseAddress,
        IReadOnlyList<ExcelParameterMapping> mappings) =>
        ValidateConfiguration(baseAddress, mappings, out _);

    private static List<string> ValidateConfiguration(
        string baseAddress,
        IReadOnlyList<ExcelParameterMapping> mappings,
        out string normalizedBaseAddress)
    {
        var errors = new List<string>();
        normalizedBaseAddress = baseAddress.Trim();
        if (normalizedBaseAddress.Length == 0)
        {
            errors.Add("请输入基础地址。");
        }
        else if (normalizedBaseAddress.Any(char.IsWhiteSpace) ||
                 !Uri.TryCreate(normalizedBaseAddress, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("基础地址必须是完整且不含空白的 http/https 地址。");
        }
        else if (uri.Fragment.Length > 0)
        {
            errors.Add("基础地址不能包含 # fragment。");
        }

        if (mappings.Count == 0)
        {
            errors.Add("请至少配置一个参数映射。");
            return errors;
        }

        var mappingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!ParameterNameRegex().IsMatch(mapping.ParameterName))
                errors.Add($"参数名“{mapping.ParameterName}”只能包含英文字母、数字和 . _ ~ -。");
            else if (!mappingNames.Add(mapping.ParameterName))
                errors.Add($"参数名“{mapping.ParameterName}”重复。");
            if (mapping.ColumnIndex <= 0 || string.IsNullOrWhiteSpace(mapping.ColumnName))
                errors.Add($"参数“{mapping.ParameterName}”尚未选择 Excel 列。");
        }

        if (errors.Count > 0 || normalizedBaseAddress.Length == 0) return errors;
        try
        {
            var queryIndex = normalizedBaseAddress.IndexOf('?');
            if (queryIndex < 0) return errors;
            var query = normalizedBaseAddress[(queryIndex + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var rawName = pair.Split('=', 2)[0];
                var decodedName = Uri.UnescapeDataString(rawName.Replace("+", " ", StringComparison.Ordinal));
                if (mappingNames.Contains(decodedName))
                    errors.Add($"参数名“{decodedName}”与基础地址中的已有参数重复。");
            }
        }
        catch (UriFormatException)
        {
            errors.Add("基础地址的查询参数包含无效转义序列。");
        }

        return errors;
    }

    private static bool IsAllowedValueRune(Rune rune) =>
        Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_' or '.' or '~';

    private static string DescribeRune(Rune rune) => rune.Value switch
    {
        ' ' => "空格",
        '\t' => "制表符",
        '\r' => "回车",
        '\n' => "换行",
        _ when Rune.IsControl(rune) => $"控制字符 U+{rune.Value:X4}",
        _ => $"“{rune}”",
    };

    private static string GetSeparator(string baseAddress)
    {
        if (!baseAddress.Contains('?')) return "?";
        if (baseAddress.EndsWith('?') || baseAddress.EndsWith('&')) return string.Empty;
        return "&";
    }
}
