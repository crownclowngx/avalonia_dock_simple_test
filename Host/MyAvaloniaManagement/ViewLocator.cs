using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement;

internal sealed class ViewLocator : IDataTemplate
{
    // 用于存储动态发现的视图
    private static readonly Dictionary<Type, Func<Control>> _dynamicViews = [];

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

            // 只复用生产组合根的不可变发现快照；目录未通过清单、deps、类型和模块结构
            // 预检的程序集不会进入 View 发现。G5 会进一步把命名扫描替换为显式 View 贡献。
            var pluginSnapshot =
                AssemblyLoaderHelper.Discover(AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
            assemblies.AddRange(pluginSnapshot.Assemblies);
            // 测试宿主和静态链接部署可能已经把托管插件加载到当前 AppDomain，
            // 但不会把它复制到生产插件目录。统一纳入已加载程序集可复用同一视图解析链路。
            assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
            assemblies = assemblies.Distinct().ToList();

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
                    foreach (var type in AssemblyTypeCatalog.GetLoadableTypes(
                                 assembly,
                                 exception => Console.Error.WriteLine(
                                     $"ViewLocator errorCode=VIEW_TYPE_SCAN_PARTIAL assembly={assembly.FullName} type={exception.GetType().Name}")))
                    {
                        if (typeof(Control).IsAssignableFrom(type) &&
                            !type.IsAbstract &&
                            type.Name.EndsWith("View"))
                        {
                            // 尝试查找对应的ViewModel类型
                            string viewModelTypeName = type.FullName?.Replace("View", "ViewModel") ?? string.Empty;
                            Type? viewModelType = assembly.GetType(viewModelTypeName);

                            if (viewModelType != null)
                            {
                                // 注册视图创建函数
                                _dynamicViews[viewModelType] = () =>
                                {
                                    try
                                    {
                                        return (Control)Activator.CreateInstance(type)!;
                                    }
                                    catch (Exception)
                                    {
                                        // 创建视图实例失败时返回一个空的 TextBlock 控件作为默认视图
                                        return new TextBlock { Text = "创建视图实例失败" };
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

        // 如果没有找到匹配的视图，尝试使用命名约定创建
        var viewTypeName = type.FullName?.Replace("ViewModel", "View") ?? string.Empty;
        var viewType = Type.GetType(viewTypeName);

        if (viewType != null && typeof(Control).IsAssignableFrom(viewType))
        {
            try
            {
                return (Control)Activator.CreateInstance(viewType)!;
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
        return data is IDockable || _dynamicViews.ContainsKey(type);
    }
}
