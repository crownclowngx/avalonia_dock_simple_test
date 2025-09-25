using System;
using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 全局服务提供者管理器
/// 提供对依赖注入容器的访问
/// </summary>
public static class ServiceProvider
{
    private static IServiceProvider? _serviceProvider;
    
    /// <summary>
    /// 初始化服务提供者
    /// </summary>
    /// <param name="serviceProvider">服务提供者实例</param>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    /// <summary>
    /// 获取指定类型的服务
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例</returns>
    /// <exception cref="InvalidOperationException">服务提供者未初始化</exception>
    public static T GetRequiredService<T>() where T : notnull
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("服务提供者尚未初始化。请先调用Initialize方法。");
        }
        
        return _serviceProvider.GetRequiredService<T>();
    }
    
    /// <summary>
    /// 获取指定类型的服务（可选）
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例，如果未找到则返回null</returns>
    public static T? GetService<T>() where T : class
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("服务提供者尚未初始化。请先调用Initialize方法。");
        }
        
        return _serviceProvider.GetService<T>();
    }
    
    /// <summary>
    /// 获取指定类型的服务
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>服务实例</returns>
    public static object GetRequiredService(Type serviceType)
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("服务提供者尚未初始化。请先调用Initialize方法。");
        }
        
        return _serviceProvider.GetRequiredService(serviceType);
    }
    
    /// <summary>
    /// 创建服务作用域
    /// </summary>
    /// <returns>服务作用域</returns>
    public static IServiceScope CreateScope()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("服务提供者尚未初始化。请先调用Initialize方法。");
        }
        
        return _serviceProvider.CreateScope();
    }
}