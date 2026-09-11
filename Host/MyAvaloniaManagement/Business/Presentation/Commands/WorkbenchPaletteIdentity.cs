using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>四种已知结果的封闭身份，避免可空 ID 的任意组合表示非法操作。</summary>
internal abstract record WorkbenchPaletteIdentity(int Order, string StableKey);
internal sealed record CommandPaletteIdentity(CommandId Id) : WorkbenchPaletteIdentity(3, "command:" + Id.Value);
internal sealed record ToolPaletteIdentity(ToolTypeId Id) : WorkbenchPaletteIdentity(2, "tool:" + Id.Value);
internal sealed record PagePaletteIdentity(WorkspacePageId Id) : WorkbenchPaletteIdentity(0, "page:" + Id.Value.ToString("N"));

/// <summary>长度前缀保证默认入口与任意合法创建意图均有无歧义的选择键；执行不用此字符串反解析。</summary>
internal sealed record FunctionPaletteIdentity(DocumentTypeId DocumentTypeId, CreationIntentId? IntentId)
    : WorkbenchPaletteIdentity(1, $"function:{DocumentTypeId.Value.Length}:{DocumentTypeId.Value}:{IntentId?.Value.Length ?? -1}:{IntentId?.Value}");
