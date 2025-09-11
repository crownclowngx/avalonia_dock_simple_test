using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using StaticViewLocator;

namespace MyAvaloniaManagement;

[StaticViewLocator]
public partial class ViewLocator : IDataTemplate
{
    // 用于存储动态发现的视图
    private static readonly Dictionary<Type, Func<Control>> _dynamicViews = new Dictionary<Type, Func<Control>>();

    static ViewLocator()
    {
        // 初始化时扫描所有程序集中的视图
        ScanForViews();
    }

    private static void ScanForViews()
    {
        try
        {
            // 获取当前程序集
            var currentAssembly = Assembly.GetExecutingAssembly();
            var assemblies = new List<Assembly> { currentAssembly };

            // 从特定子目录加载其他程序集
            var pluginAssemblies =
                AssemblyLoaderHelper.LoadPluginsFromDirectories(AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
            assemblies.AddRange(pluginAssemblies);

            foreach (var assembly in assemblies)
            {
                try
                {
                    // 跳过系统程序集
                    if (assembly.FullName?.StartsWith("System.") == true ||
                        assembly.FullName?.StartsWith("Avalonia.") == true)
                    {
                        continue;
                    }

                    // 扫描所有符合命名约定的视图类
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(Control).IsAssignableFrom(type) &&
                            !type.IsAbstract &&
                            type.Name.EndsWith("View"))
                        {
                            // 尝试查找对应的ViewModel类型
                            string viewModelTypeName = type.FullName.Replace("View", "ViewModel");
                            Type? viewModelType = assembly.GetType(viewModelTypeName);

                            if (viewModelType != null)
                            {
                                // 注册视图创建函数
                                _dynamicViews[viewModelType] = () =>
                                {
                                    try
                                    {
                                        return (Control)Activator.CreateInstance(type);
                                    }
                                    catch (Exception)
                                    {
                                        return null;
                                    }
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"扫描程序集 {assembly.FullName} 中的视图时出错: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"扫描视图时发生错误: {ex.Message}");
        }
    }

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var type = data.GetType();

        // 首先尝试使用动态发现的视图
        if (_dynamicViews.TryGetValue(type, out var dynamicFunc))
        {
            var control = dynamicFunc.Invoke();
            if (control != null)
            {
                return control;
            }
        }

        if (s_views.TryGetValue(type, out var func))
        {
            return func.Invoke();
        }

        // 如果没有找到匹配的视图，尝试使用命名约定创建
        var viewTypeName = type.FullName.Replace("ViewModel", "View");
        var viewType = Type.GetType(viewTypeName);

        if (viewType != null && typeof(Control).IsAssignableFrom(viewType))
        {
            try
            {
                return (Control)Activator.CreateInstance(viewType);
            }
            catch (Exception)
            {
                // 创建失败时忽略
            }
        }

        // 如果是Dockable对象但没有对应的视图，创建一个默认视图
        if (data is IDockable dockable)
        {
            return new TextBlock { Text = $"未找到 {dockable.Title} 的视图" };
        }

        throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data)
    {
        if (data is null)
        {
            return false;
        }

        var type = data.GetType();
        return data is IDockable || _dynamicViews.ContainsKey(type) || s_views.ContainsKey(type);
    }
}