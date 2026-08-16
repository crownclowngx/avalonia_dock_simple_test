using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace MyAvaloniaManagementCommon.Behaviors;

/// <summary>
/// 一个能够处理已标记为"已处理"事件的通用行为类
/// 可以绑定任何RoutedEvent到ViewModel中的命令
/// </summary>
public class HandledEventsAwareBehavior : Behavior<Control>
{
    // 行为尚未附加、事件名称无效或事件注册失败时，这两个反射对象都可能为空。
    // 显式建模为空可以避免注销阶段因部分初始化而触发新的空引用异常。
    private Delegate? _handler;
    private EventInfo? _eventInfo;

    #region 依赖属性

    /// <summary>标识 <see cref="EventName"/> 的 Avalonia 样式属性。</summary>
    public static readonly StyledProperty<string> EventNameProperty =
        AvaloniaProperty.Register<HandledEventsAwareBehavior, string>(nameof(EventName));

    /// <summary>标识 <see cref="Command"/> 的 Avalonia 样式属性。</summary>
    public static readonly StyledProperty<ICommand> CommandProperty =
        AvaloniaProperty.Register<HandledEventsAwareBehavior, ICommand>(nameof(Command));

    /// <summary>标识 <see cref="CommandParameter"/> 的 Avalonia 样式属性。</summary>
    public static readonly StyledProperty<object> CommandParameterProperty =
        AvaloniaProperty.Register<HandledEventsAwareBehavior, object>(nameof(CommandParameter));

    /// <summary>标识 <see cref="HandledEventsToo"/> 的 Avalonia 样式属性。</summary>
    public static readonly StyledProperty<bool> HandledEventsTooProperty =
        AvaloniaProperty.Register<HandledEventsAwareBehavior, bool>(nameof(HandledEventsToo), true);

    #endregion

    #region 属性访问器

    /// <summary> 
    /// 要处理的事件名称 
    /// </summary> 
    public string EventName
    {
        get => GetValue(EventNameProperty);
        set => SetValue(EventNameProperty, value);
    }

    /// <summary> 
    /// 要执行的命令 
    /// </summary> 
    public ICommand Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary> 
    /// 命令参数 
    /// </summary> 
    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary> 
    /// 是否处理已标记为已处理的事件 
    /// 默认值为true 
    /// </summary> 
    public bool HandledEventsToo
    {
        get => GetValue(HandledEventsTooProperty);
        set => SetValue(HandledEventsTooProperty, value);
    }

    #endregion

    #region 生命周期方法

    /// <summary> 
    /// 当行为附加到控件时调用 
    /// </summary> 
    protected override void OnAttached()
    {
        base.OnAttached();

        // 注册事件 
        RegisterEvent();

        // 监听属性变化 
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary> 
    /// 当行为从控件分离时调用 
    /// </summary> 
    protected override void OnDetaching()
    {
        base.OnDetaching();

        // 移除事件监听 
        PropertyChanged -= OnPropertyChanged;

        // 注销事件处理器 
        UnregisterEvent();

        // 清理引用 
        _handler = null;
        _eventInfo = null;
    }

    #endregion

    #region 私有方法

    /// <summary> 
    /// 注册事件处理器 
    /// </summary> 
    private void RegisterEvent()
    {
        if (AssociatedObject != null && !string.IsNullOrEmpty(EventName))
        {
            try
            {
                // 获取事件信息 
                _eventInfo = GetEventInfo(EventName);
                _handler = _eventInfo == null ? null : CreateEventHandler(_eventInfo);

                if (_handler != null && _eventInfo != null)
                {
                    // 对于支持handledEventsToo的事件，使用AddHandler方法 
                    if (HandledEventsToo && typeof(Interactive).IsAssignableFrom(AssociatedObject.GetType()))
                    {
                        if (HandledEventsToo && AssociatedObject is Interactive interactive)
                        {
                            // 直接处理常见的路由事件，避免反射查找AddHandler方法
                            if (EventName == InputElement.PointerPressedEvent.Name)
                            {
                                interactive.AddHandler(InputElement.PointerPressedEvent,
                                    new EventHandler<PointerPressedEventArgs>(ExecuteCommandWithEventArgs),
                                    handledEventsToo: HandledEventsToo);
                                return;
                            }
                            else if (EventName == InputElement.PointerReleasedEvent.Name)
                            {
                                interactive.AddHandler(InputElement.PointerReleasedEvent,
                                    new EventHandler<PointerReleasedEventArgs>(ExecuteCommandWithEventArgs),
                                    handledEventsToo: HandledEventsToo);
                                return;
                            }
                        }
                    }

                    // 对于不支持handledEventsToo的事件，直接使用事件添加器 
                    _eventInfo.AddEventHandler(AssociatedObject, _handler);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"注册事件处理器失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 注销事件处理器
    /// </summary>
    private void UnregisterEvent()
    {
        if (AssociatedObject != null)
        {
            try
            {
                if (HandledEventsToo && AssociatedObject is Interactive interactive)
                {
                    // 直接处理常见的路由事件
                    if (EventName == InputElement.PointerPressedEvent.Name)
                    {
                        interactive.RemoveHandler(InputElement.PointerPressedEvent,
                            new EventHandler<PointerPressedEventArgs>(ExecuteCommandWithEventArgs));
                        return;
                    }
                    else if (EventName == InputElement.PointerReleasedEvent.Name)
                    {
                        interactive.RemoveHandler(InputElement.PointerReleasedEvent,
                            new EventHandler<PointerReleasedEventArgs>(ExecuteCommandWithEventArgs));
                        return;
                    }
                }

                // 对于其他事件，直接使用事件移除器
                if (_eventInfo != null && _handler != null)
                {
                    _eventInfo.RemoveEventHandler(AssociatedObject, _handler);
                }
            }
            catch (Exception ex)
            {
                // 在实际应用中，您可能需要使用日志框架记录异常
                System.Diagnostics.Debug.WriteLine($"注销事件处理器失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 根据事件名称获取事件信息
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <returns>事件信息对象</returns>
    private EventInfo? GetEventInfo(string eventName)
    {
        // 尝试从常见的事件拥有者类型中查找事件
        var eventOwnerTypes = new[]
        {
            typeof(Control),
            typeof(InputElement),
            typeof(Interactive),
            typeof(Visual),
            AssociatedObject?.GetType() // 尝试使用关联对象的实际类型
        };

        foreach (var type in eventOwnerTypes)
        {
            if (type == null)
                continue;

            // 获取事件信息
            var eventInfo = type.GetEvent(eventName,
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.FlattenHierarchy);

            if (eventInfo != null)
                return eventInfo;
        }

        return null;
    }

    /// <summary>
    /// 根据事件名称获取路由事件（如果有）
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <returns>路由事件对象</returns>
    private RoutedEvent? GetRoutedEvent(string eventName)
    {
        // 尝试从常见的事件拥有者类型中查找路由事件字段
        var eventOwnerTypes = new[]
        {
            typeof(Control),
            typeof(InputElement),
            typeof(Interactive),
            typeof(Visual),
            AssociatedObject?.GetType() // 尝试使用关联对象的实际类型
        };

        foreach (var type in eventOwnerTypes)
        {
            if (type == null)
                continue;

            // 尝试获取路由事件字段
            var routedEventField = type.GetField($"{eventName}Event",
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.FlattenHierarchy);

            if (routedEventField != null)
            {
                var routedEvent = routedEventField.GetValue(null) as RoutedEvent;
                if (routedEvent != null)
                    return routedEvent;
            }
        }

        return null;
    }

    /// <summary>
    /// 创建适合事件委托类型的处理器
    /// </summary>
    /// <param name="eventInfo">事件信息</param>
    /// <returns>事件处理器委托</returns>
    private Delegate? CreateEventHandler(EventInfo eventInfo)
    {
        try
        {
            // 获取事件处理程序的委托类型
            var handlerType = eventInfo.EventHandlerType;
            if (handlerType == null)
                return null;

            // 获取委托的方法签名
            var invokeMethod = handlerType.GetMethod("Invoke");
            if (invokeMethod == null)
                return null;

            // 创建一个通用的事件处理方法，该方法将调用命令
            var parameters = invokeMethod.GetParameters();

            // 创建一个lambda表达式，该表达式接受事件参数并调用命令
            Delegate handler;

            if (parameters.Length == 2)
            {
                // 标准的事件处理程序签名: (sender, e)
                var method = typeof(HandledEventsAwareBehavior).GetMethod(
                    "ExecuteCommandWithEventArgs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    return null;
                }

                handler = Delegate.CreateDelegate(
                    handlerType,
                    this,
                    method);
            }
            else
            {
                // 非标准的事件处理程序签名
                var method = typeof(HandledEventsAwareBehavior).GetMethod(
                    "ExecuteCommand",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    return null;
                }

                handler = Delegate.CreateDelegate(
                    handlerType,
                    this,
                    method);
            }

            return handler;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"创建事件处理器失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 执行命令的通用方法（带事件参数）
    /// </summary>
    private void ExecuteCommandWithEventArgs(object? sender, EventArgs e)
    {
        ExecuteCommandInternal();
    }

    /// <summary>
    /// 执行命令的通用方法
    /// </summary>
    private void ExecuteCommand()
    {
        ExecuteCommandInternal();
    }

    /// <summary>
    /// 执行命令的内部方法
    /// </summary>
    private void ExecuteCommandInternal()
    {
        if (Command != null && Command.CanExecute(CommandParameter))
        {
            Command.Execute(CommandParameter);
        }
    }

    /// <summary>
    /// 属性变化处理
    /// </summary>
    /// <param name="sender">发送者</param>
    /// <param name="e">属性变化参数</param>
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 如果EventName或HandledEventsToo属性发生变化，重新注册事件
        if (e.Property == EventNameProperty || e.Property == HandledEventsTooProperty)
        {
            UnregisterEvent();
            RegisterEvent();
        }
    }

    #endregion
}
