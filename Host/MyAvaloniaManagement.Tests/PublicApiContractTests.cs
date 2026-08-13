using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 锁定 Host 与 Common 程序集导出的公共 API，避免普通内部重构意外改变插件契约。
/// 本次指纹更新对应已经评审的保存契约升级：新增 IDocumentSaveState，要求可保存
/// Document 明确报告并提交脏状态；后续实现调整不得顺带修改此指纹。
/// </summary>
public sealed class PublicApiContractTests
{
    private const string ExpectedSha256 =
        "0CCBB254B3C5A542A9388AA79DE6CDDFA58537206F57C3E08CB0A81E8FED2814";

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
