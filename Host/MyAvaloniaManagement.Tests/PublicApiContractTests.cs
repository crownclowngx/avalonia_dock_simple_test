using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 在 G13 建立可审阅 API 文本基线前，暂时锁定唯一正式插件契约程序集 Common。
/// Host 是可执行实现程序集，不再与 SDK 拼接为同一个不可读指纹。
/// </summary>
public sealed class PublicApiContractTests
{
    private const string ExpectedSha256 =
        "0CB827AD85465575877C8B7B797694FE23616CE7200E8B98E60380470CFF7E75";

    [Fact]
    public void PluginSdkPublicApiSurfaceRemainsStable()
    {
        var lines = GetPublicSurface(typeof(IPluginModule).Assembly)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
        var payload = string.Join('\n', lines);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        Assert.True(
            string.Equals(ExpectedSha256, hash, StringComparison.Ordinal),
            $"Public API SHA256: {hash}");
    }

    [Fact]
    public void PluginSdk事件总线只暴露Sdk自有类型和Bcl令牌()
    {
        var eventBusType = typeof(IHostEventBus);
        var methods = eventBusType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(["Publish", "Subscribe"], methods.Select(method => method.Name).Order().ToArray());
        Assert.DoesNotContain(
            typeof(IPluginModule).Assembly.ExportedTypes
                .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Select(member => member.ToString()),
            signature => signature?.Contains(
                "CommunityToolkit.Mvvm.Messaging",
                StringComparison.Ordinal) == true);
        Assert.Equal(typeof(IDisposable), methods.Single(method => method.Name == "Subscribe").ReturnType);
        var assembly = typeof(IPluginModule).Assembly;
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.IMessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessengerService"));
        Assert.Null(assembly.GetType("MyAvaloniaManagementCommon.Message.MessageHandler`2"));
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
