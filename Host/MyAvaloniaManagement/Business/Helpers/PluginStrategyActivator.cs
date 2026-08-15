using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 根据插件程序集是否显式接入模块机制，选择创建策略实例的兼容路径。
/// <para>
/// 历史插件始终使用 <see cref="Activator.CreateInstance(Type)"/> 和公共无参构造函数，
/// 保持原有构造函数选择与初始化时机；只有托管插件程序集才允许从宿主容器解析构造参数。
/// </para>
/// </summary>
internal static class PluginStrategyActivator
{
    /// <summary>
    /// 创建一个 Document 或 Tool 策略。调用方负责先验证类型确实实现目标策略接口。
    /// </summary>
    public static TStrategy Create<TStrategy>(
        Type strategyType,
        Assembly assembly,
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog)
        where TStrategy : class
    {
        if (pluginModuleCatalog.IsManaged(assembly))
        {
            return (TStrategy)ActivatorUtilities.CreateInstance(serviceProvider, strategyType);
        }

        if (strategyType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new InvalidOperationException(
                $"历史插件策略 {strategyType.FullName} 必须保留公共无参构造函数。");
        }

        return (TStrategy)Activator.CreateInstance(strategyType)!;
    }
}
