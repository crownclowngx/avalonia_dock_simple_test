using System.Diagnostics.CodeAnalysis;

namespace MyAvaloniaManagement.PluginSdk;

/// <summary>表示一个可跨菜单、快捷键和命令面板稳定引用的工作台命令身份。</summary>
/// <remarks>
/// 命令身份只表达用户语义，不保存 <c>ICommand</c> 实例、执行回调或
/// 工作台状态。值对象只负责词法正确性；命令是否属于某个插件，由 Host 在注册 Seal 时统一验证。
/// </remarks>
public sealed record CommandId
{
    /// <summary>使用经过稳定标识规则校验的字符串创建命令身份。</summary>
    /// <param name="value">长度为 1–128 的小写点分/kebab-case 字符串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足稳定标识规则。</exception>
    public CommandId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value), allowDots: true);

    /// <summary>获取注册表和展示贡献共同使用的规范字符串。</summary>
    public string Value { get; }

    /// <summary>解析一个命令身份；非法输入通过异常明确拒绝。</summary>
    /// <param name="value">待解析的规范字符串。</param>
    /// <returns>具有值相等语义的命令身份。</returns>
    public static CommandId Parse(string value) => new(value);

    /// <summary>尝试解析命令身份，不把预期输入错误转换为异常。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="commandId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足稳定标识规则时为 true，否则为 false。</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out CommandId? commandId)
    {
        commandId = StableIdentifierRules.TryValidate(value, true, out var validated)
            ? new CommandId(validated)
            : null;
        return commandId is not null;
    }

    /// <summary>返回可直接用于注册表和诊断白名单的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>描述一个 Document Target 中单条命令的可执行状态已经变化。</summary>
/// <remarks>
/// 一次事件只携带一个命令身份，不使用 null 或“全部刷新”哨兵。同一业务变化影响多条命令时，
/// Target 应逐条发出事件。事件可以来自插件工作线程；Host 的展示层负责切换到 UI 线程并去重。
/// </remarks>
public sealed class WorkbenchCommandStateChangedEventArgs : EventArgs
{
    /// <summary>创建一条定向状态变化通知。</summary>
    /// <param name="commandId">状态已经变化的非 null 命令身份。</param>
    /// <exception cref="ArgumentNullException"><paramref name="commandId"/> 为 null。</exception>
    public WorkbenchCommandStateChangedEventArgs(CommandId commandId) =>
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));

    /// <summary>获取状态已经变化的唯一命令身份。</summary>
    public CommandId CommandId { get; }
}

/// <summary>定义当前插件 Document 实例可选择实现的工作台命令目标。</summary>
/// <remarks>
/// Target 就是当前 Document 模型实例的窄能力，不拥有 Catalog、Context、Provider 或 Dock。
/// <see cref="CanExecute"/> 应短小且无阻塞；真正执行必须返回可等待的任务并观察取消令牌，避免
/// 通过 async void 或未观察任务让 Host 误判命令已经完成。
/// </remarks>
public interface IWorkbenchDocumentCommandTarget
{
    /// <summary>当当前实例中某一条命令的可执行状态变化时发生。</summary>
    /// <remarks>事件可以从工作线程发出；订阅者必须负责线程切换和成对退订。</remarks>
    event EventHandler<WorkbenchCommandStateChangedEventArgs>? CommandStateChanged;

    /// <summary>查询当前 Document 实例此刻是否允许执行指定命令。</summary>
    /// <param name="commandId">要查询的稳定命令身份。</param>
    /// <returns>当前实例能够立即接受该命令时为 true，否则为 false。</returns>
    bool CanExecute(CommandId commandId);

    /// <summary>在当前 Document 实例上异步执行指定命令。</summary>
    /// <param name="commandId">要执行的稳定命令身份。</param>
    /// <param name="cancellationToken">调用取消、Document 关闭或 Host 退出时使用的协作取消令牌。</param>
    /// <returns>表示命令真实完成的可等待操作。</returns>
    ValueTask ExecuteAsync(CommandId commandId, CancellationToken cancellationToken);
}
