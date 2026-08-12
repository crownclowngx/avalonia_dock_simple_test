using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;

namespace MySmallTools.Constants;

public static class DocumentTypeIdConstant
{
    public static readonly PluginId PluginId = new("myavalonia.plugin.my-small-tools");
    public static readonly DocumentTypeId SecretVideoDocumentId =
        new("myavalonia.plugin.my-small-tools.document.secret-video-player");
    public static readonly DocumentTypeId LegacySecretVideoDocumentId =
        new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    public static readonly DocumentTypeId VideoEncryptorDocumentId =
        new("myavalonia.plugin.my-small-tools.document.video-encryptor");
    public static readonly DocumentTypeId LegacyVideoEncryptorDocumentId =
        new("B2C3D4E5-F6G7-8901-BCDE-F23456789012");
    public static readonly DocumentTypeId SecretVideoLibraryDocumentId =
        new("myavalonia.plugin.my-small-tools.document.secret-video-library");
    public static readonly DocumentTypeId LegacySecretVideoLibraryDocumentId =
        new("C3D4E5F6-A7B8-4901-CDEF-345678901234");
    public static readonly DocumentTypeId VideoDecryptorDocumentId =
        new("myavalonia.plugin.my-small-tools.document.video-decryptor");
    public static readonly DocumentTypeId LegacyVideoDecryptorDocumentId =
        new("D4E5F6A7-B8C9-4A12-DEF0-456789012345");
}
