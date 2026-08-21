using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 为一个已验证 Managed Plugin 提供组合阶段的受控注册入口。
/// </summary>
/// <remarks>
/// <para>
/// 上下文只在宿主构建根级依赖注入容器时有效。插件必须在
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
    /// 获取用于注册插件私有业务服务的根级服务集合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该集合只允许追加由当前插件拥有的私有业务服务。插件不得删除、替换、重排既有描述符，
    /// 也不得为宿主已经注册的服务类型追加实现；宿主会在构建根容器前以事务方式校验并提交增量。
    /// </para>
    /// <para>
    /// 插件可以为自己的接口登记多个 singleton、scoped、transient、keyed 或开放泛型实现。
    /// 不得通过此集合直接注册 Document/Tool 策略或插件生命周期；这些宿主可见贡献必须使用
    /// 本接口的专用方法。模块返回后保存并修改该集合不产生任何宿主注册效果。
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
