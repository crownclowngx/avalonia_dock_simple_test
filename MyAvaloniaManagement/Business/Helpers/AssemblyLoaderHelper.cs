using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MyAvaloniaManagement.Business.Helpers;

public static class AssemblyLoaderHelper
{
    // 存储每个插件目录对应的AssemblyLoadContext
    private static readonly ConcurrentDictionary<string, PluginLoadContext> _pluginContexts = new();
    // 存储已加载的程序集
    private static readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new();
    
    private static readonly ConcurrentDictionary<string, List<Assembly>> _loadedPluginAssemblies = new();
    // 记录程序集解析事件是否已注册
    private static bool _assemblyResolveHandlerRegistered = false;

    /// <summary>
    /// 从应用程序执行目录下的特定子目录加载所有插件项目
    /// 每个插件项目位于独立的子文件夹中，并使用独立的AssemblyLoadContext
    /// </summary>
    /// <param name="rootPluginsDirName">根插件目录名称</param>
    /// <returns>加载的程序集列表</returns>
    public static List<Assembly> LoadPluginsFromDirectories(string rootPluginsDirName)
    {
        if (_loadedPluginAssemblies.TryGetValue(rootPluginsDirName, out var loadedAssemblies))
        {
            return loadedAssemblies;
        }
        // 注册程序集解析事件处理程序
        if (!_assemblyResolveHandlerRegistered)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => CurrentDomain_AssemblyResolve(sender!, args);
            _assemblyResolveHandlerRegistered = true;
        }
        loadedAssemblies ??= [];
        try
        {
            // 获取应用程序基目录
            string appBaseDir = AppContext.BaseDirectory;
            
            // 构建根插件目录的完整路径
            string rootPluginsDir = Path.Combine(appBaseDir, rootPluginsDirName);
            
            // 检查根插件目录是否存在，如果不存在则创建
            if (!Directory.Exists(rootPluginsDir))
            {
                Directory.CreateDirectory(rootPluginsDir);
                return loadedAssemblies;
            }

            // 获取根插件目录下的所有子目录（每个子目录是一个独立的插件项目）
            string[] pluginDirectories = Directory.GetDirectories(rootPluginsDir);
            
            // 加载每个插件项目
            foreach (string pluginDir in pluginDirectories)
            {
                // 为每个插件创建独立的加载上下文
                var loadContext = new PluginLoadContext(pluginDir);
                _pluginContexts.TryAdd(pluginDir, loadContext);
                
                // 递归加载插件目录及其子目录中的所有DLL
                var pluginAssemblies = LoadAssembliesRecursively(pluginDir, loadContext);
                loadedAssemblies.AddRange(pluginAssemblies);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载插件目录时发生错误: {ex.Message}");
        }
        _loadedPluginAssemblies.TryAdd(rootPluginsDirName, loadedAssemblies);
        return loadedAssemblies;
    }
    
    /// <summary>
    /// 递归加载指定目录及其子目录中的所有DLL程序集
    /// </summary>
    /// <param name="directoryPath">要扫描的目录路径</param>
    /// <param name="loadContext">用于加载程序集的AssemblyLoadContext</param>
    /// <returns>加载的程序集列表</returns>
    private static List<Assembly> LoadAssembliesRecursively(string directoryPath, PluginLoadContext loadContext)
    {
        var loadedAssemblies = new List<Assembly>();
        
        try
        {
            // 获取当前目录中的所有DLL文件
            string[] dllFiles = Directory.GetFiles(directoryPath, "*.dll");
            
            foreach (string dllFile in dllFiles)
            {
                try
                {
                    // 从文件路径创建程序集名称
                    string assemblyName = Path.GetFileNameWithoutExtension(dllFile);
                    
                    // 避免重复加载
                    if (_loadedAssemblies.ContainsKey(assemblyName))
                    {
                        continue;
                    }

                    // 使用指定的AssemblyLoadContext加载程序集
                    Assembly assembly = loadContext.LoadFromAssemblyPath(dllFile);
                    loadedAssemblies.Add(assembly);
                    _loadedAssemblies.TryAdd(assemblyName, assembly);
                }
                catch (Exception ex)
                {
                    // 记录错误但继续加载其他程序集
                    Console.WriteLine($"加载程序集 {Path.GetFileName(dllFile)} 时出错: {ex.Message}");
                }
            }

            // 递归处理子目录
            string[] subdirectories = Directory.GetDirectories(directoryPath);
            foreach (string subdir in subdirectories)
            {
                var subdirAssemblies = LoadAssembliesRecursively(subdir, loadContext);
                loadedAssemblies.AddRange(subdirAssemblies);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"递归加载程序集时发生错误: {ex.Message}");
        }

        return loadedAssemblies;
    }
    
    /// <summary>
    /// 程序集解析事件处理程序，用于解析插件依赖的第三方库
    /// </summary>
    private static Assembly? CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        try
        {
            // 检查是否已经加载了该程序集
            string? assemblyName = new AssemblyName(args.Name).Name;
            if (assemblyName == null) {
                return null;
            }
            if (_loadedAssemblies.TryGetValue(assemblyName, out var loadedAssembly))
            {
                return loadedAssembly;
            }

            // 如果是在解析插件依赖，让每个插件的AssemblyLoadContext尝试解析
            foreach (var pluginContext in _pluginContexts.Values)
            {
                try
                {
                    Assembly? assembly = pluginContext.ResolveAssembly(args.Name);
                    if (assembly != null)
                    {
                        return assembly;
                    }
                }
                catch { /* 忽略特定上下文的解析错误，继续尝试其他上下文 */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"程序集解析失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 从应用程序执行目录下的特定子目录加载所有DLL程序集
    /// </summary>
    /// <param name="subdirectoryName">子目录名称</param>
    /// <returns>加载的程序集列表</returns>
    public static List<Assembly> LoadAssembliesFromSubdirectory(string subdirectoryName)
    {
        var loadedAssemblies = new List<Assembly>();
        
        try
        {
            // 获取应用程序基目录
            string appBaseDir = AppContext.BaseDirectory;
            
            // 构建子目录的完整路径
            string pluginsDir = Path.Combine(appBaseDir, subdirectoryName);
            
            // 检查子目录是否存在，如果不存在则创建
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return loadedAssemblies;
            }
            
            // 获取子目录中的所有DLL文件
            string[] dllFiles = Directory.GetFiles(pluginsDir, "*.dll");
            
            foreach (string dllFile in dllFiles)
            {
                try
                {
                    // 加载程序集
                    Assembly assembly = Assembly.LoadFrom(dllFile);
                    loadedAssemblies.Add(assembly);
                }
                catch (Exception ex)
                {
                    // 记录错误但继续加载其他程序集
                    Console.WriteLine($"加载程序集 {Path.GetFileName(dllFile)} 时出错: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载子目录程序集时发生错误: {ex.Message}");
        }
        
        return loadedAssemblies;
    }
}