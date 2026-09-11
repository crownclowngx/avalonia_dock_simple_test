using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>保存菜单分类的展示路径；它只描述分类，绝不参与文件访问或 Document 类型解析。</summary>
/// <remarks>
/// 原始分类仍由注册描述保存，旧版菜单不经过本解析器。新版只认斜杠，非法空段整体回退，
/// 避免看似宽容的“删除空段”把错误元数据悄悄归入另一个业务分类。
/// </remarks>
internal sealed class DocumentCategoryPath
{
    private DocumentCategoryPath(string[] segments, bool valid)
    {
        Segments = Array.AsReadOnly(segments);
        IsValid = valid;
        CanonicalPath = string.Join("/", segments);
        DisplayPath = string.Join(" / ", segments);
        // 长度前缀使回退后含斜杠的单段名称也具有独立身份，不与合法的多段路径发生碰撞。
        Key = string.Join("|", segments.Select(segment => $"{segment.Length}:{segment}"));
    }

    internal IReadOnlyList<string> Segments { get; }
    internal string Key { get; }
    internal string CanonicalPath { get; }
    internal string DisplayPath { get; }
    internal bool IsValid { get; }

    internal static DocumentCategoryPath Parse(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        var segments = category.Split('/').Select(segment => segment.Trim()).ToArray();
        var valid = segments.All(segment => segment.Length > 0);
        return new(valid ? segments : [category], valid);
    }

    /// <summary>根据已经验证的分段创建父路径，不重新解释段内的保留字符。</summary>
    internal static DocumentCategoryPath FromSegments(IEnumerable<string> segments) => new(segments.ToArray(), true);
}
