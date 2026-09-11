using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Icons;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation.Icons;

/// <summary>所有者来自贡献快照，不从图标字符串反推。Host 内建入口使用空所有者。</summary>
internal sealed record HostIconRequest(PluginId? OwnerId, string? Reference);

/// <summary>已完成归属校验的纯数据；几何对象只在 UI 适配器中创建。</summary>
internal sealed record ResolvedHostIcon(string Reference, VectorIconDefinition Definition);

/// <summary>本次 Runtime 的只读图标查询；公共资源和插件贡献采用同一数据格式。</summary>
/// <remarks>
/// 资源包负责图形，Registry 负责声明提交，此处只负责查询及降级，不扩展成可写全局管理器。
/// 公共资源按 All 自动导入，增加公共图标不需要在 Host 再维护一份名称映射。
/// </remarks>
internal sealed class HostIconCatalog
{
    internal const string DefaultKey = "builtin:module";
    private readonly Dictionary<string, ResolvedHostIcon> _builtins;
    private readonly Dictionary<string, PluginIconRegistration> _plugins;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly HashSet<(PluginId?, string?, string)> _reported = [];

    public HostIconCatalog(PluginRegistry registry, PluginAvailabilityReadModel availability,
        IHostDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _diagnostics = diagnostics;
        _plugins = registry.Icons.ToDictionary(icon => icon.Reference, StringComparer.Ordinal);
        _builtins = CommonIcons.All.ToDictionary(asset => asset.Key,
            asset => new ResolvedHostIcon(asset.Key, new VectorIconDefinition(
                asset.PathData, asset.ViewBoxWidth, asset.ViewBoxHeight)), StringComparer.Ordinal);
    }

    internal event EventHandler<PluginAvailabilityChangedEventArgs>? Changed
    {
        add => _availability.AvailabilityChanged += value;
        remove => _availability.AvailabilityChanged -= value;
    }

    internal ResolvedHostIcon Default => _builtins[DefaultKey];

    internal ResolvedHostIcon Resolve(HostIconRequest? request)
    {
        // 每次查询都先核对可用性，再允许进入 UI 几何缓存；缓存不能复活已禁用插件。
        if (request?.OwnerId is { } owner && !_availability.IsAvailable(owner)) return Default;
        if (string.IsNullOrWhiteSpace(request?.Reference)) return Default;
        if (_builtins.TryGetValue(request.Reference, out var builtin)) return builtin;
        if (_plugins.TryGetValue(request.Reference, out var plugin))
        {
            if (request.OwnerId == plugin.OwnerId)
                return new ResolvedHostIcon(plugin.Reference, plugin.Definition);
            ReportOnce(request, "ICON_OWNER_MISMATCH");
        }
        else ReportOnce(request, "ICON_REFERENCE_NOT_FOUND");
        return Default;
    }

    /// <summary>只记录稳定引用与错误码；不记录整段几何、插件对象或外部资源路径内容。</summary>
    internal void ReportOnce(HostIconRequest request, string code)
    {
        if (_reported.Add((request.OwnerId, request.Reference, code)))
            _diagnostics?.Report(new HostDiagnosticDraft(code, HostDiagnosticPhase.IconPresentation)
            {
                PluginId = request.OwnerId,
                StableId = request.Reference?.StartsWith("plugin:", StringComparison.Ordinal) == true ||
                           request.Reference?.StartsWith("builtin:", StringComparison.Ordinal) == true
                    ? request.Reference : null,
            });
    }
}
