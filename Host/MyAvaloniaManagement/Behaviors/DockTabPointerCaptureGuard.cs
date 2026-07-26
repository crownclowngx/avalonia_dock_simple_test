using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MyAvaloniaManagement.Behaviors;

/// <summary>
/// 为 Dock 标签拖动补齐跨控件、跨顶层窗口时的指针所有权。
/// </summary>
/// <remarks>
/// Dock 12.0.0.2 的标签排序辅助器只记录了逻辑捕获状态，没有在按下阶段真正捕获指针。
/// 本保护层只保证松开或捕获丢失事件能够返回原标签，Dock 仍然独立负责排序、浮动和停靠。
/// </remarks>
internal sealed class DockTabPointerCaptureGuard : AvaloniaObject
{
    private const string RecoveryDiagnosticCode = "DOCK_DRAG_RECOVERY_STALE_VISUAL";

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DockTabPointerCaptureGuard, Control, bool>(
            "IsEnabled");

    private static readonly AttachedProperty<GuardState?> StateProperty =
        AvaloniaProperty.RegisterAttached<DockTabPointerCaptureGuard, Control, GuardState?>(
            "State");

    static DockTabPointerCaptureGuard()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private DockTabPointerCaptureGuard()
    {
    }

    public static bool GetIsEnabled(Control control) =>
        control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) =>
        control.SetValue(IsEnabledProperty, value);

    internal static bool IsAttached(Control control) =>
        control.GetValue(StateProperty) is not null;

    internal static bool ShouldCapture(
        bool isLeftButtonPressed,
        bool hasForeignCapture,
        bool isButtonSource) =>
        isLeftButtonPressed &&
        !hasForeignCapture &&
        !isButtonSource;

    internal static bool RecoverStaleVisualState(
        Control control,
        ITransform? originalRenderTransform)
    {
        var pseudoClasses = (IPseudoClasses)control.Classes;
        if (!pseudoClasses.Contains(":dragging"))
        {
            return false;
        }

        // 只在 Dock 未完成自己的清理时兜底，避免覆盖一次正常拖拽已经提交的视觉状态。
        control.SetCurrentValue(Visual.RenderTransformProperty, originalRenderTransform);
        control.ClearValue(Panel.ZIndexProperty);
        pseudoClasses.Remove(":dragging");
        return true;
    }

    private static void OnIsEnabledChanged(
        Control control,
        AvaloniaPropertyChangedEventArgs args)
    {
        var enabled = args.GetNewValue<bool>();
        var current = control.GetValue(StateProperty);

        if (enabled)
        {
            if (current is not null)
            {
                return;
            }

            var state = new GuardState(control);
            control.SetValue(StateProperty, state);
            state.Attach();
            return;
        }

        if (current is null)
        {
            return;
        }

        current.Dispose();
        control.ClearValue(StateProperty);
    }

    private sealed class GuardState : IDisposable
    {
        private readonly Control _owner;
        private IPointer? _activePointer;
        private ITransform? _originalRenderTransform;
        private Window? _topLevelWindow;
        private int _interactionVersion;
        private int _scheduledRecoveryVersion = -1;
        private bool _isAttached;
        private bool _isDisposed;

        public GuardState(Control owner)
        {
            _owner = owner;
        }

        public void Attach()
        {
            if (_isAttached)
            {
                return;
            }

            _owner.AddHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.PointerCaptureLostEvent,
                OnPointerCaptureLost,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AttachedToVisualTree += OnAttachedToVisualTree;
            _owner.DetachedFromVisualTree += OnDetachedFromVisualTree;

            _isAttached = true;
            BindTopLevel();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            ReleaseOwnedPointer();
            RecoverImmediatelyIfNeeded();
            UnbindTopLevel();

            if (_isAttached)
            {
                _owner.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                _owner.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
                _owner.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
                _owner.AttachedToVisualTree -= OnAttachedToVisualTree;
                _owner.DetachedFromVisualTree -= OnDetachedFromVisualTree;
                _isAttached = false;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            var currentPoint = e.GetCurrentPoint(_owner);
            var hasForeignCapture =
                e.Pointer.Captured is not null &&
                !ReferenceEquals(e.Pointer.Captured, _owner);
            var isButtonSource = IsButtonSource(e.Source as Visual);

            if (!ShouldCapture(
                    currentPoint.Properties.IsLeftButtonPressed,
                    hasForeignCapture,
                    isButtonSource))
            {
                return;
            }

            _interactionVersion++;
            _scheduledRecoveryVersion = -1;
            _originalRenderTransform = _owner.RenderTransform;
            _activePointer = e.Pointer;

            // 不设置 Handled；捕获仅补齐输入所有权，Dock 原有状态机必须继续收到同一个按下事件。
            e.Pointer.Capture(_owner);
            if (!ReferenceEquals(e.Pointer.Captured, _owner))
            {
                _activePointer = null;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!ReferenceEquals(e.Pointer, _activePointer))
            {
                return;
            }

            ReleaseOwnedPointer();
            ScheduleVisualRecovery();
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!ReferenceEquals(e.Pointer, _activePointer))
            {
                return;
            }

            // Dock 把捕获权交给 DockControl 时属于正常路径；延迟检查可让 Dock 先完成自己的清理。
            _activePointer = null;
            ScheduleVisualRecovery();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            BindTopLevel();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            ReleaseOwnedPointer();
            RecoverImmediatelyIfNeeded();
            UnbindTopLevel();
        }

        private void OnTopLevelDeactivated(object? sender, EventArgs e)
        {
            if (_activePointer is null)
            {
                return;
            }

            ReleaseOwnedPointer();
            ScheduleVisualRecovery();
        }

        private void BindTopLevel()
        {
            var topLevelWindow = TopLevel.GetTopLevel(_owner) as Window;
            if (ReferenceEquals(topLevelWindow, _topLevelWindow))
            {
                return;
            }

            UnbindTopLevel();
            _topLevelWindow = topLevelWindow;
            if (_topLevelWindow is not null)
            {
                _topLevelWindow.Deactivated += OnTopLevelDeactivated;
            }
        }

        private void UnbindTopLevel()
        {
            if (_topLevelWindow is not null)
            {
                _topLevelWindow.Deactivated -= OnTopLevelDeactivated;
                _topLevelWindow = null;
            }
        }

        private void ReleaseOwnedPointer()
        {
            var pointer = _activePointer;
            _activePointer = null;
            if (pointer is not null &&
                ReferenceEquals(pointer.Captured, _owner))
            {
                pointer.Capture(null);
            }
        }

        private void ScheduleVisualRecovery()
        {
            var version = _interactionVersion;
            if (_scheduledRecoveryVersion == version)
            {
                return;
            }

            _scheduledRecoveryVersion = version;
            _owner.Dispatcher.Post(
                () =>
                {
                    if (_isDisposed || version != _interactionVersion)
                    {
                        return;
                    }

                    _scheduledRecoveryVersion = -1;
                    if (RecoverStaleVisualState(_owner, _originalRenderTransform))
                    {
                        Trace.TraceWarning(RecoveryDiagnosticCode);
                    }
                },
                DispatcherPriority.Background);
        }

        private void RecoverImmediatelyIfNeeded()
        {
            _interactionVersion++;
            _scheduledRecoveryVersion = -1;
            if (RecoverStaleVisualState(_owner, _originalRenderTransform))
            {
                Trace.TraceWarning(RecoveryDiagnosticCode);
            }
        }

        private bool IsButtonSource(Visual? source)
        {
            for (var current = source;
                 current is not null;
                 current = current.GetVisualParent())
            {
                if (current is Button)
                {
                    return true;
                }

                if (ReferenceEquals(current, _owner))
                {
                    break;
                }
            }

            return false;
        }
    }
}
