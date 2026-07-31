using System;
using Avalonia;
using Avalonia.Styling;

namespace MyAvaloniaManagement.Business.Appearance;

/// <summary>
/// 协调用户主题偏好、Avalonia 全局主题变体和本地持久化。
/// </summary>
internal sealed class ApplicationThemeService
{
    private readonly AppearanceSettingsStore _settingsStore;
    private Application? _application;

    public ApplicationThemeService(AppearanceSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ??
            throw new ArgumentNullException(nameof(settingsStore));
        CurrentMode = _settingsStore.Load();
    }

    internal ApplicationThemeMode CurrentMode { get; private set; }

    /// <summary>
    /// 绑定当前 Avalonia 应用，并在创建任何窗口前应用已保存的主题。
    /// </summary>
    internal void Initialize(Application application)
    {
        _application = application ??
            throw new ArgumentNullException(nameof(application));
        ApplyToApplication(CurrentMode);
    }

    /// <summary>
    /// 立即应用并保存新的主题偏好。
    /// </summary>
    internal void SetMode(ApplicationThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        CurrentMode = mode;
        ApplyToApplication(mode);
        _settingsStore.Save(mode);
    }

    internal static ThemeVariant ToThemeVariant(
        ApplicationThemeMode mode) =>
        mode switch
        {
            ApplicationThemeMode.System => ThemeVariant.Default,
            ApplicationThemeMode.Light => ThemeVariant.Light,
            ApplicationThemeMode.Dark => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private void ApplyToApplication(ApplicationThemeMode mode)
    {
        if (_application is not null)
        {
            _application.RequestedThemeVariant = ToThemeVariant(mode);
        }
    }
}
