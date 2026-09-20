using System;
using System.Collections.Generic;
using System.Reflection;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 把底层加载异常稳定映射为宿主错误码。
/// </summary>
internal static class PluginLoadExceptionMapper
{
    internal static string GetCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.TryPop(out var current))
        {
            if (current.Message.Contains(
                    HostDiagnosticCodes.PluginSharedAssemblyMismatch,
                    StringComparison.Ordinal))
            {
                return HostDiagnosticCodes.PluginSharedAssemblyMismatch;
            }

            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }

            if (current is ReflectionTypeLoadException reflection)
            {
                foreach (var loaderException in reflection.LoaderExceptions)
                {
                    if (loaderException is not null)
                    {
                        pending.Push(loaderException);
                    }
                }
            }
        }

        return HostDiagnosticCodes.PluginAssemblyLoadFailed;
    }
}
