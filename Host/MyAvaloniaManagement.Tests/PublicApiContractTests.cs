using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 锁定 Host 与 Common 程序集导出的元数据表面，使内部重构不会意外改变外部契约。
/// 只有经过单独评审的契约变更才能更新该指纹，避免实现调整掩盖签名变化。
/// </summary>
public sealed class PublicApiContractTests
{
    private const string ExpectedSha256 =
        "D49EB41F46B08311229B3F8D6D3FCD6D014F1750E018952620562FA8634A0D56";

    [Fact]
    public void HostAndCommonPublicApiSurfaceRemainsStable()
    {
        var lines = new[]
            {
                typeof(MainWindowViewModel).Assembly,
                typeof(IPluginModule).Assembly
            }
            .Distinct()
            .SelectMany(GetPublicSurface)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
        var payload = string.Join('\n', lines);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        Assert.True(
            string.Equals(ExpectedSha256, hash, StringComparison.Ordinal),
            $"Public API SHA256: {hash}");
    }

    private static IEnumerable<string> GetPublicSurface(Assembly assembly)
    {
        foreach (var type in assembly.ExportedTypes
                     .OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            var typeName = FormatType(type);
            yield return $"T|{typeName}|{type.Attributes}";

            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                yield return $"C|{typeName}|{FormatParameters(constructor)}";
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly)
                     .Where(item => !item.IsSpecialName))
            {
                yield return
                    $"M|{typeName}|{method.Name}|{method.GetGenericArguments().Length}|{FormatParameters(method)}|{FormatType(method.ReturnType)}";
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                yield return
                    $"P|{typeName}|{property.Name}|{FormatType(property.PropertyType)}|get:{property.GetMethod?.IsPublic == true}|set:{property.SetMethod?.IsPublic == true}";
            }

            foreach (var field in type.GetFields(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                yield return
                    $"F|{typeName}|{field.Name}|{FormatType(field.FieldType)}|literal:{field.IsLiteral}|readonly:{field.IsInitOnly}";
            }

            foreach (var @event in type.GetEvents(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                yield return
                    $"E|{typeName}|{@event.Name}|{FormatType(@event.EventHandlerType!)}";
            }
        }
    }

    private static string FormatParameters(MethodBase method) =>
        string.Join(
            ",",
            method.GetParameters().Select(parameter =>
                $"{FormatType(parameter.ParameterType)}:{parameter.IsOut}:{parameter.IsOptional}"));

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            return FormatType(type.GetElementType()!) + "&";
        }

        if (type.IsArray)
        {
            return FormatType(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName
                             ?? type.GetGenericTypeDefinition().Name;
        definitionName = definitionName[..definitionName.IndexOf('`')];
        return $"{definitionName}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }
}
