using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 提取程序集内仍可使用的类型。
/// 可选依赖导致局部类型加载失败时只排除失败类型，避免丢弃整个插件程序集。
/// </summary>
internal static class AssemblyTypeCatalog
{
    internal static IReadOnlyList<Type> GetLoadableTypes(
        Assembly assembly,
        Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            reportFailure?.Invoke(exception);
            return exception.Types
                .Where(type => type is not null)
                .Cast<Type>()
                .ToArray();
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
            return [];
        }
    }
}
