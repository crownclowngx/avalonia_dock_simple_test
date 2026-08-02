namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// G4: 破坏性操作确认服务接口。
/// 设计思考：ROADMAP 明确要求"批量删除、重来等破坏性操作会展示任务数量并要求确认"。
/// 通过 DIP 将确认逻辑抽象为接口，实现以下目标：
/// 1. ViewModel 不直接依赖 Avalonia 对话框 API，保持可测试性；
/// 2. 测试中可注入 Fake 实现，验证"确认通过"和"确认拒绝"两条路径；
/// 3. 未来可替换为内联确认条、Toast 确认等不同 UX 形态，不改动 VM 代码。
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// 向用户展示确认对话框，等待用户决策。
    /// </summary>
    /// <param name="title">对话框标题（如"批量删除确认"）</param>
    /// <param name="message">确认消息正文（应包含操作数量和影响描述）</param>
    /// <returns>true 表示用户确认执行，false 表示用户取消</returns>
    Task<bool> ConfirmAsync(string title, string message);
}

/// <summary>
/// 未注入确认服务时的空实现（始终返回 true）。
/// 设计思考：保持与 G2 的 NullCredentialProvider 相同的向后兼容模式——
/// 构造函数参数可选（= null），未注入时 fallback 到此实现，
/// 确保现有测试和调用点无需修改即可编译通过。
/// 注意：生产环境应注入真实的 Avalonia 对话框实现。
/// </summary>
public sealed class NullConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
}
