using System;
using System.Linq;

namespace MyAvaloniaManagement.Business.Search;

/// <summary>为功能、工具及命令搜索提供一致的纯文本匹配等级。</summary>
/// <remarks>
/// 仅比较调用方提供的展示元数据，不读取插件、配置、时钟或使用历史。
/// 名称优先于辅助字段，使用户完整输入名称时不会被说明中偶然出现的词挤到后面。
/// 空查询交由各视图保留原有顺序；不匹配返回最大值，避免引入索引服务或策略注册框架。
/// </remarks>
internal static class WorkbenchTextMatch
{
    internal static int Rank(string name, string? query, params string[] details)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0 || name.Equals(text, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(text, StringComparison.OrdinalIgnoreCase)) return 2;
        return details.Any(detail => detail.Contains(text, StringComparison.OrdinalIgnoreCase)) ? 3 : int.MaxValue;
    }
}
