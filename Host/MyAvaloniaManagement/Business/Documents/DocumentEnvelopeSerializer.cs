using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 封装现有 <see cref="DocumentSaveData"/> 的 Newtonsoft 序列化规则。
/// 将格式细节隔离在文档工作流之外，确保重构期间继续读写同一种历史 JSON 契约。
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
