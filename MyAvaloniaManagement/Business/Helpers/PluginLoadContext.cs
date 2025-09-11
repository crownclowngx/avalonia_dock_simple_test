using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 为每个插件创建的独立AssemblyLoadContext
/// 用于隔离不同插件的依赖项，解决版本冲突问题
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginPath;
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();

    public PluginLoadContext(string pluginPath)
    {
        _pluginPath = pluginPath;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 尝试从已加载的程序集中查找
        if (_loadedAssemblies.TryGetValue(assemblyName.Name, out var assembly))
        {
            return assembly;
        }

        // 尝试从插件目录加载程序集
        string assemblyPath = FindAssemblyInDirectory(_pluginPath, assemblyName.Name + ".dll");
        if (assemblyPath != null)
        {
            try
            {
                assembly = LoadFromAssemblyPath(assemblyPath);
                _loadedAssemblies[assemblyName.Name] = assembly;
                return assembly;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"在插件加载上下文中加载程序集 {assemblyName.Name} 时出错: {ex.Message}");
            }
        }

        // 如果找不到，回退到默认加载行为（从应用程序基目录或GAC加载）
        return null;
    }

    /// <summary>
    /// 尝试解析指定名称的程序集
    /// </summary>
    /// <param name="assemblyName">要解析的程序集名称</param>
    /// <returns>解析到的程序集，如果未找到则返回null</returns>
    public Assembly? ResolveAssembly(string assemblyName)
    {
        try
        {
            return Load(new AssemblyName(assemblyName));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在指定目录及其子目录中查找指定名称的程序集文件
    /// </summary>
    /// <param name="directoryPath">要搜索的目录路径</param>
    /// <param name="assemblyFileName">要查找的程序集文件名</param>
    /// <returns>找到的程序集文件路径，如果未找到则返回null</returns>
    private string? FindAssemblyInDirectory(string directoryPath, string assemblyFileName)
    {
        try
        {
            // 检查当前目录
            string filePath = Path.Combine(directoryPath, assemblyFileName);
            if (File.Exists(filePath))
            {
                return filePath;
            }

            // 递归检查子目录
            foreach (string subdir in Directory.GetDirectories(directoryPath))
            {
                string? foundPath = FindAssemblyInDirectory(subdir, assemblyFileName);
                if (foundPath != null)
                {
                    return foundPath;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"搜索程序集时出错: {ex.Message}");
        }

        return null;
    }
}