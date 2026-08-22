using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>定义 manifest v2 指定的唯一 Managed Plugin 模块入口。</summary>
/// <remarks>模块只在组合阶段执行一次，并必须在返回前完成全部注册；它不自报身份，也不扫描程序集。</remarks>
public interface IPluginModule
{
    /// <summary>向当前插件独占的注册入口声明私有服务与宿主可见贡献。</summary>
    /// <param name="registration">由 Host 为当前 manifest 身份创建且仅在组合阶段有效的注册入口。</param>
    /// <remarks>实现应在返回前同步完成声明，不得保存注册入口供运行期修改。</remarks>
    void Configure(IPluginRegistration registration);
}

/// <summary>为一个已验证插件提供窄而显式的组合入口。</summary>
/// <remarks>
/// <see cref="Services"/> 只属于当前插件，模块进入时为空；专用方法同时冻结模型、View 和描述符。
/// 模块返回并通过 Host 校验后，Host 才追加端口及贡献根的固定生命周期注册。两条路径分离可防止
/// 普通 DI 注册被误解释为 UI 贡献，也使插件无法用 Remove/Replace 改变 Host 拥有的协议底座。
/// </remarks>
public interface IPluginRegistration
{
    /// <summary>获取 manifest 声明并由 Host 验证的只读插件身份。</summary>
    PluginId PluginId { get; }

    /// <summary>获取仅用于当前插件私有对象图的服务集合。</summary>
    /// <remarks>
    /// 插件可以使用标准 DI 的生命周期、开放泛型、keyed 和多实现能力，但不得手工登记 Host Port
    /// 或已通过专用方法声明的贡献根类型。模块返回后集合永久封闭，保存引用不能用于运行期修改。
    /// </remarks>
    IServiceCollection Services { get; }

    /// <summary>登记一个插件级单例生命周期；同一插件至多登记一个。</summary>
    /// <typeparam name="TLifecycle">由插件容器构造并拥有的生命周期实现类型。</typeparam>
    /// <remarks>
    /// 实现约定为插件级 singleton；描述符由 Host 在模块返回、所有权校验通过后最终追加，重复声明
    /// 或通过 <see cref="Services"/> 手工登记根类型都会在构建插件 Provider 前拒绝。
    /// </remarks>
    void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle;

    /// <summary>登记不可保存的 scoped Document 模型及其每次新建的 Avalonia View。</summary>
    /// <typeparam name="TDocument">每个 Document Scope 拥有一个实例的普通模型类型。</typeparam>
    /// <typeparam name="TView">由 Host 为该模型创建的 Avalonia 控件类型。</typeparam>
    /// <param name="descriptor">构造时已冻结的身份与展示元数据。</param>
    /// <exception cref="ArgumentNullException">描述符为 null。</exception>
    /// <remarks>Host 在 Seal 后验证 ID 归属，并最终追加模型的 scoped 注册。</remarks>
    void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPluginDocument
        where TView : Control, new();

    /// <summary>登记可保存的 scoped Document 模型及其每次新建的 Avalonia View。</summary>
    /// <typeparam name="TDocument">每个 Document Scope 拥有一个实例的可保存模型类型。</typeparam>
    /// <typeparam name="TView">由 Host 为该模型创建的 Avalonia 控件类型。</typeparam>
    /// <param name="descriptor">构造时已冻结的身份与展示元数据。</param>
    /// <exception cref="ArgumentNullException">描述符为 null。</exception>
    /// <remarks>Host 在 Seal 后验证 ID 归属，并最终追加模型的 scoped 注册。</remarks>
    void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPersistablePluginDocument
        where TView : Control, new();

    /// <summary>登记插件级单例 Tool 模型及其每次新建的 Avalonia View。</summary>
    /// <typeparam name="TTool">由插件 Provider 创建并持有到插件关闭的普通模型类型。</typeparam>
    /// <typeparam name="TView">由 Host 创建、但不拥有 Tool 模型生命周期的 Avalonia 控件类型。</typeparam>
    /// <param name="descriptor">构造时已冻结的身份、位置和关闭语义。</param>
    /// <exception cref="ArgumentNullException">描述符为 null。</exception>
    /// <remarks>Host 在 Seal 后验证 ID 归属，并最终追加模型的 singleton 注册。</remarks>
    void AddTool<TTool, TView>(ToolDescriptor descriptor)
        where TTool : class
        where TView : Control, new();
}
