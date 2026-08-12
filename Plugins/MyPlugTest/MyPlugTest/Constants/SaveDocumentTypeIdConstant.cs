using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyPlugTest.Constants;

public static class SaveDocumentTypeIdConstant
{
    public static readonly PluginId PluginId = new("myavalonia.plugin.my-plug-test");
    public static readonly DocumentTypeId TestWelcomeDocumentId =
        new("myavalonia.plugin.my-plug-test.document.welcome");
    public static readonly DocumentTypeId LegacyTestWelcomeDocumentId =
        new("7DEE4212-DFF1-9923-B527-1B047D1B2918");
    public static readonly DocumentTypeId TestMessageReceiveDocumentId =
        new("myavalonia.plugin.my-plug-test.document.message-receiver");
    public static readonly DocumentTypeId LegacyTestMessageReceiveDocumentId =
        new("384D28C4-F6E8-4D49-B0BD-2CE484D4D177");
    public static readonly DocumentTypeId BatchHttpGetDocumentId =
        new("myavalonia.plugin.my-plug-test.document.batch-http-get");
    public static readonly DocumentTypeId LegacyBatchHttpGetDocumentId =
        new("C1B13C72-C21A-4C39-9612-77C341DA85B6");
    public static readonly ToolTypeId CustomToolId =
        new("myavalonia.plugin.my-plug-test.tool.custom");
    public static readonly ToolTypeId LegacyCustomToolId = new("MyCustomTool");
}
