using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 封装当前 <see cref="DocumentSaveData"/> 信封的 Newtonsoft 序列化规则。
/// 将 JSON 细节隔离在文档工作流之外；本类型只读写当前契约，不承担旧字段猜测或版本迁移。
/// </summary>
internal sealed class DocumentEnvelopeSerializer
{
    internal DocumentSaveData Deserialize(string content) =>
        JsonConvert.DeserializeObject<DocumentSaveData>(content)
        ?? throw new DocumentLoadException(
            "文档信封为空，无法识别文档类型。");

    internal string Serialize(DocumentSaveData saveData) =>
        JsonConvert.SerializeObject(saveData, Formatting.Indented);
}
