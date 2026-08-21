using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 为一个已验证 Managed Plugin 提供组合阶段的私有容器注册入口。
/// </summary>
/// <remarks>
/// <para>
/// 上下文只在宿主构建当前插件独占的依赖注入容器时有效。插件必须在
/// <see cref="IPluginModule.Configure"/> 返回前完成全部登记；宿主不支持运行期追加、移除、
/// 热更新或热卸载贡献。
/// </para>
/// <para>
/// <see cref="Services"/> 用于插件自己的业务依赖，四个 <c>Add*</c> 方法用于宿主必须理解的
/// Document、Tool、View 与生命周期贡献。刻意分开这两条路径，可以防止普通 DI 注册被误当作
/// 用户界面扩展，也让宿主在执行业务代码前验证所有权、稳定 ID 和重复映射。
/// </para>
/// </remarks>
public interface IPluginRegistrationContext
{
    /// <summary>
    /// 获取清单声明并已经由宿主验证的稳定插件身份。
    /// </summary>
    /// <remarks>该值只读；插件不能用代码覆盖清单身份。</remarks>
    PluginId PluginId { get; }

    /// <summary>
    /// 获取只属于当前插件的服务集合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该集合从新的空集合建立，只预置宿主明确支持的窄端口和当前插件的 Document Scope 基础设施。
    /// 它不是宿主集合的副本，也不包含其他插件描述符；修改或清空它最多使当前插件构建失败。
    /// </para>
    /// <para>
    /// 插件可以为自己的接口登记多个 singleton、scoped、transient、keyed 或开放泛型实现。
    /// 通过此集合直接注册 Document/Tool 策略或插件生命周期不会使其进入宿主 Registry；宿主可见贡献
    /// 必须使用本接口的专用方法。模块返回后 Provider 已由当前集合建立，继续修改保存的集合引用无效。
    /// </para>
    /// </remarks>
    IServiceCollection Services { get; }

    /// <summary>
    /// 登记一个由宿主在 Registry 构建阶段创建并长期持有的 Document 策略。
    /// </summary>
    /// <typeparam name="TStrategy">实现 Document 创建契约的具体策略类型。</typeparam>
    void AddDocument<TStrategy>() where TStrategy : class, IDocumentCreationStrategy;

    /// <summary>
    /// 登记一个由宿主在 Registry 构建阶段创建并长期持有的 Tool 策略。
    /// </summary>
    /// <typeparam name="TStrategy">实现 Tool 创建契约的具体策略类型。</typeparam>
    void AddTool<TStrategy>() where TStrategy : class, IToolCreationStrategy;

    /// <summary>
    /// 显式关联一个 ViewModel 类型和每次显示时新建的 Avalonia View。
    /// </summary>
    /// <typeparam name="TViewModel">作为 DataTemplate 数据源的精确运行时类型。</typeparam>
    /// <typeparam name="TView">
    /// 具有公共无参构造的控件类型。运行依赖应放入 ViewModel，避免根容器持有瞬态可释放控件。
    /// </typeparam>
    void AddView<TViewModel, TView>() where TView : Control, new();

    /// <summary>
    /// 登记一个由宿主按依赖计划初始化并在退出时反向关闭的插件级生命周期。
    /// </summary>
    /// <typeparam name="TLifecycle">实现生命周期契约的具体类型。</typeparam>
    /// <remarks>
    /// 生命周期实例按插件进程生命周期作为单例创建。没有常驻后台职责的插件不应登记空实现。
    /// </remarks>
    void AddLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle;
}
