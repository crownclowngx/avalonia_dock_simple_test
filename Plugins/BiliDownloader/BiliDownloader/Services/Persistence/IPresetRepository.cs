using BiliDownloader.Models;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 预设仓储接口：管理下载预设的增删查。
/// <para>
/// 设计思考：预设数量极少（内置 3 个 + 自定义 < 20 个），不需要复杂的查询能力。
/// 接口仅暴露 4 个方法，保持精简（ISP）。
/// 内置预设始终从代码获取（BuiltInPresets.GetAll()），不写入数据库，避免污染。
/// </para>
/// </summary>
public interface IPresetRepository
{
    /// <summary>
    /// 获取所有预设（内置 + 自定义合并返回）。
    /// 内置预设始终排在前面，自定义预设按创建顺序排列。
    /// </summary>
    Task<List<DownloadPreset>> GetAllAsync();

    /// <summary>
    /// 按 ID 获取单个预设（含内置预设查找）。
    /// </summary>
    /// <param name="id">预设 ID</param>
    /// <returns>预设实例，未找到时返回 null</returns>
    Task<DownloadPreset?> GetByIdAsync(string id);

    /// <summary>
    /// 保存自定义预设（新增或覆盖）。
    /// 内置预设（IsBuiltIn=true）不允许通过此方法覆盖。
    /// </summary>
    Task SaveAsync(DownloadPreset preset);

    /// <summary>
    /// 删除自定义预设。内置预设拒绝删除（静默忽略）。
    /// </summary>
    /// <param name="id">预设 ID</param>
    Task DeleteAsync(string id);
}
