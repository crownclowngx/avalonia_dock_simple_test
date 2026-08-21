namespace NoModule.Plugin;

/// <summary>
/// 模拟清单指定了可公开构造的普通类型，但该类型没有实现 V2 <c>IPluginModule</c>。
/// </summary>
/// <remarks>
/// 构造函数故意抛出异常，证明 Loader 必须在结构预检阶段拒绝错误入口，不能先实例化再判断。
/// 本夹具不引用任何旧契约，从而把测试目标限定为 V2 入口边界，而不是延续 Legacy 编译面。
/// </remarks>
public sealed class NotAPluginModule
{
    public NotAPluginModule() =>
        throw new System.InvalidOperationException("错误入口类型不应被实例化。");
}
