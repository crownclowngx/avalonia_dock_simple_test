using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using DocumentTypeId = MyAvaloniaManagement.PluginSdk.DocumentTypeId;

namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 将内部诊断草稿转换为可长期保存的白名单记录。
/// </summary>
/// <remarks>
/// 设计意图：草稿位于异常捕获边界，可能携带插件异常和未经验证的目录信息；记录则会同时进入
/// 内存界面、JSON Lines 和默认镜像，必须在两者之间完成一次不可绕过的收窄。这里不做关键词替换，
/// 因为密码、正文和签名地址没有可靠的通用词法特征；只复制已经具备明确格式的字段更容易审计。
/// </remarks>
internal static class HostDiagnosticRedactionPolicy
{
    private const int MaximumTokenLength = 128;

    internal static HostDiagnosticRecord Create(
        Guid sessionId,
        HostDiagnosticDraft draft,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var hasValidCode = IsSafeErrorCode(draft.Code);
        var code = hasValidCode
            ? draft.Code
            : HostDiagnosticCodes.DiagnosticInputRejected;
        var classification = HostDiagnosticFailurePolicy.Classify(code, draft.Phase);
        return new HostDiagnosticRecord
        {
            SessionId = sessionId,
            Sequence = 0,
            TimestampUtc = timestampUtc,
            Code = code,
            Severity = classification.Severity,
            Phase = draft.Phase,
            Disposition = classification.Disposition,
            PluginId = draft.PluginId?.Value,
            PluginDirectory = ToSafeLeafToken(draft.PluginDirectory),
            AssemblyName = ToSafeAssemblySimpleName(draft.AssemblyName),
            StableId = ToSafeStableId(draft.StableId),
            PluginVersion = draft.PluginVersion is null
                ? null
                : PluginVersionText.Format(draft.PluginVersion),
            SdkRange = draft.SdkRange?.ToString(),
            UserMessage = CreateUserMessage(code, draft.Phase),
            ExceptionType = draft.Exception?.GetType().FullName,
            TechnicalDetail = CreateControlledDetail(draft),
        };
    }

    /// <summary>
    /// 仅根据宿主拥有的错误码和阶段生成用户说明。
    /// </summary>
    /// <remarks>
    /// 这里故意不接收调用点文本。清单属性名、入口名称、插件类型名等内容即使经过局部校验，
    /// 仍然可能由插件或文件控制；集中固定映射可以保证 UI 与 JSONL 不会因后续调用点疏忽而泄漏。
    /// 未知但格式合法的错误码也只得到阶段级固定说明，错误码本身仍保留，便于定位新增分支。
    /// </remarks>
    private static string CreateUserMessage(string code, HostDiagnosticPhase phase) => code switch
    {
        HostDiagnosticCodes.DiagnosticInputRejected =>
            "诊断输入未通过白名单校验，原始输入未被保存。",
        HostDiagnosticCodes.PersistenceUnavailable =>
            "诊断持久化暂不可用，本次会话仍保留受控内存记录。",
        HostDiagnosticCodes.PluginRootScanFailed =>
            "无法完成插件根目录扫描，宿主不能确认本次启动的插件集合。",
        "PLUGIN_ENABLEMENT_RECOVERED" or "PLUGIN_ENABLEMENT_SCHEMA_UNSUPPORTED" or
        "PLUGIN_ENABLEMENT_INVALID" or "PLUGIN_ENABLEMENT_READ_FAILED" or
        "PLUGIN_ENABLEMENT_CONFLICT" or "PLUGIN_ENABLEMENT_WRITE_FAILED" =>
            MyAvaloniaManagement.Business.Plugins.Enablement.PluginEnablementMessages.ForCode(code),
        HostDiagnosticCodes.PluginManifestMissing or
        HostDiagnosticCodes.PluginManifestInvalid or
        HostDiagnosticCodes.PluginManifestSchemaUnsupported or
        HostDiagnosticCodes.PluginSdkIncompatible or
        HostDiagnosticCodes.PluginManifestIdentityDuplicate or
        HostDiagnosticCodes.PluginManifestDescriptionMismatch =>
            "插件清单未通过预检，已隔离对应插件候选。",
        HostDiagnosticCodes.PluginEntryInvalid or
        HostDiagnosticCodes.PluginDependencyManifestMissing or
        HostDiagnosticCodes.PluginAssemblyLoadFailed or
        HostDiagnosticCodes.PluginSharedAssemblyMismatch or
        HostDiagnosticCodes.PluginTypePreflightFailed =>
            "插件入口或类型未通过预检，已隔离对应插件候选。",
        HostDiagnosticCodes.PluginServiceRegistrationFailed =>
            "插件显式注册失败，已隔离该插件，宿主与其他插件继续运行。",
        HostDiagnosticCodes.PluginHostServiceRegistrationForbidden or
        HostDiagnosticCodes.PluginContributionServiceRegistrationForbidden =>
            "插件登记了由宿主保留的服务类型，已在容器构建前隔离该插件。",
        HostDiagnosticCodes.DocumentIdOwnerMismatch or
        HostDiagnosticCodes.ToolIdOwnerMismatch =>
            "插件贡献 ID 不属于清单声明的插件命名空间，已隔离该插件。",
        HostDiagnosticCodes.PluginContainerBuildFailed =>
            "插件私有依赖注入容器构建失败，已隔离该插件。",
        HostDiagnosticCodes.HostContainerBuildFailed =>
            "宿主依赖注入容器构建失败，主工作台不能安全启动。",
        HostDiagnosticCodes.ExtensionDiscoveryFailed or
        HostDiagnosticCodes.ExtensionActivationFailed =>
            "扩展贡献激活或校验失败，主工作台不能安全启动。",
        HostDiagnosticCodes.ToolAdapterActivationFailed =>
            "Tool 适配或视图创建失败，已隔离该 Tool，其他工作区继续运行。",
        HostDiagnosticCodes.ToolLayoutOperationFailed =>
            "工具布局操作或状态通知失败，请重新查看当前工具状态。",
        HostDiagnosticCodes.LifecycleInitializeFailed or
        HostDiagnosticCodes.LifecycleInitializeTimeout =>
            "插件初始化失败或超时，已隔离该插件贡献。",
        HostDiagnosticCodes.LifecycleShutdownFailed or
        HostDiagnosticCodes.LifecycleShutdownTimeout =>
            "插件关闭失败或超时，宿主将继续检查其余关闭项并判定资源是否可以释放。",
        HostDiagnosticCodes.LifecycleOperationRetained =>
            "生命周期或取消通知尚未结束，宿主已保留 Provider。",
        HostDiagnosticCodes.LifecycleFailedShutdownRetained =>
            "Shutdown 已失败或取消，宿主按保守政策保留 Provider。",
        HostDiagnosticCodes.LifecycleShutdownSkipped =>
            "成功初始化项尚未执行必要 Shutdown，宿主已保留 Provider。",
        HostDiagnosticCodes.LifecycleDrainCheckFailed =>
            "生命周期关闭检查失败，宿主无法证明安全，已保留 Provider。",
        HostDiagnosticCodes.LifecycleHostCancelled =>
            "宿主取消了插件初始化。",
        HostDiagnosticCodes.LifecycleCancellationFailed =>
            "插件生命周期操作失败。",
        HostDiagnosticCodes.WorkflowActionShutdownTimeout =>
            "Workflow Action 在关闭宽限内没有退出，宿主已阻止不安全的 Provider 释放。",
        HostDiagnosticCodes.WorkbenchCommandExecutionFailed =>
            "工作台命令执行失败；异常正文未写入诊断。",
        HostDiagnosticCodes.WorkbenchCommandTargetStateFailed =>
            "工作台命令目标状态查询失败；插件异常正文未写入诊断。",
        HostDiagnosticCodes.WorkbenchCommandTargetSubscriptionFailed =>
            "工作台命令目标状态订阅失败；插件异常正文未写入诊断。",
        HostDiagnosticCodes.WorkbenchCommandStateObserverFailed =>
            "工作台命令状态观察者失败；异常正文未写入诊断。",
        HostDiagnosticCodes.WorkbenchKeyGestureConflict =>
            "工作台快捷键与 Host 保留项或其他插件冲突，冲突绑定均未激活。",
        HostDiagnosticCodes.WorkbenchCommandDocumentCloseCancellationFailed =>
            "Document 命令关闭取消回调失败；异常正文未写入诊断。",
        HostDiagnosticCodes.WorkbenchCommandShutdownTimeout =>
            "工作台命令在关闭宽限内没有退出，宿主已阻止不安全的工作区和 Provider 释放。",
        HostDiagnosticCodes.HostStartupCleanupFailed =>
            "启动失败后的资源清理发生异常，请查看启动失败信息；部分资源可能保留至进程退出。",
        HostDiagnosticCodes.HostStartupUnexpected =>
            "宿主启动发生未分类异常，主工作台没有启动。",
        "VIEW_CREATION_FAILED" =>
            "已登记的插件视图创建失败。",
        HostDiagnosticCodes.PluginModuleActivationFailed =>
            "插件模块无法通过公共无参构造创建。",
        _ when phase == HostDiagnosticPhase.Layout =>
            "布局恢复或保存失败，宿主已使用安全回退并保留诊断。",
        _ when phase == HostDiagnosticPhase.PluginLifecycle =>
            "插件生命周期操作失败。",
        _ when phase == HostDiagnosticPhase.WorkflowAction =>
            "Workflow Action 调用失败；参数正文和插件异常未写入诊断。",
        _ when phase == HostDiagnosticPhase.WorkbenchCommand =>
            "工作台命令执行失败；异常正文未写入诊断。",
        _ when phase == HostDiagnosticPhase.IconPresentation =>
            "图标不可用，已显示公共默认图标；业务功能仍然可用。",
        _ => "宿主操作失败，原始输入未被保存。",
    };

    private static string? CreateControlledDetail(HostDiagnosticDraft draft)
    {
        if (draft.LifecycleStage is null && draft.Duration is null)
        {
            return null;
        }

        var parts = new List<string>(capacity: 2);
        if (draft.LifecycleStage is { } stage)
        {
            parts.Add($"stage={stage}");
        }

        if (draft.Duration is { } duration)
        {
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "durationMs={0:0.###}",
                duration.TotalMilliseconds));
        }

        return string.Join("; ", parts);
    }

    private static bool IsSafeErrorCode(string? value) =>
        IsSafeToken(value, allowLowercase: false);

    private static string? ToSafeStableId(string? value) =>
        DocumentTypeId.TryParse(value, out var stableId)
            ? stableId!.Value
            : null;

    private static string? ToSafeLeafToken(string? value)
    {
        if (!IsSafeToken(value, allowLowercase: true) ||
            Path.IsPathRooted(value!) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static string? ToSafeAssemblySimpleName(AssemblyName? value)
    {
        var simpleName = value?.Name;
        return IsSafeToken(simpleName, allowLowercase: true)
            ? simpleName
            : null;
    }

    private static bool IsSafeToken(string? value, bool allowLowercase)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTokenLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterUpper(character) ||
            allowLowercase && char.IsAsciiLetterLower(character) ||
            char.IsAsciiDigit(character) ||
            character is '_' or '-' or '.');
    }
}
