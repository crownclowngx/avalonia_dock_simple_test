namespace MyAvaloniaManagement.Business.Diagnostics;

/// <summary>
/// 集中定义稳定错误码，避免业务阶段继续散落无法检索的字符串字面量。
/// </summary>
internal static class HostDiagnosticCodes
{
    internal const string DiagnosticInputRejected = "HOST_DIAGNOSTIC_INPUT_REJECTED";
    internal const string PersistenceUnavailable = "DIAGNOSTIC_PERSISTENCE_UNAVAILABLE";
    internal const string PluginRootScanFailed = "PLUGIN_ROOT_SCAN_FAILED";
    internal const string PluginManifestMissing = "PLUGIN_MANIFEST_MISSING";
    internal const string PluginManifestInvalid = "PLUGIN_MANIFEST_INVALID";
    internal const string PluginManifestSchemaUnsupported = "PLUGIN_MANIFEST_SCHEMA_UNSUPPORTED";
    internal const string PluginSdkIncompatible = "PLUGIN_SDK_INCOMPATIBLE";
    internal const string PluginManifestIdentityDuplicate = "PLUGIN_MANIFEST_IDENTITY_DUPLICATE";
    internal const string PluginManifestDescriptionMismatch = "PLUGIN_MANIFEST_DESCRIPTION_MISMATCH";
    internal const string PluginEntryInvalid = "PLUGIN_ENTRY_INVALID";
    internal const string PluginDependencyManifestMissing = "PLUGIN_DEPENDENCY_MANIFEST_MISSING";
    internal const string PluginAssemblyLoadFailed = "PLUGIN_ASSEMBLY_LOAD_FAILED";
    internal const string PluginSharedAssemblyMismatch = "PLUGIN_SHARED_ASSEMBLY_MISMATCH";
    internal const string PluginTypePreflightFailed = "PLUGIN_TYPE_PREFLIGHT_FAILED";
    internal const string PluginModuleActivationFailed = "PLUGIN_MODULE_ACTIVATION_FAILED";
    internal const string PluginServiceRegistrationFailed = "PLUGIN_SERVICE_REGISTRATION_FAILED";
    internal const string PluginHostServiceRegistrationForbidden =
        "PLUGIN_HOST_SERVICE_REGISTRATION_FORBIDDEN";
    internal const string PluginContributionServiceRegistrationForbidden =
        "PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN";
    internal const string DocumentIdOwnerMismatch = "DOCUMENT_ID_OWNER_MISMATCH";
    internal const string ToolIdOwnerMismatch = "TOOL_ID_OWNER_MISMATCH";
    internal const string WorkbenchCommandIdOwnerMismatch =
        "WORKBENCH_COMMAND_ID_OWNER_MISMATCH";
    internal const string WorkbenchCommandTargetDocumentOwnerMismatch =
        "WORKBENCH_COMMAND_TARGET_DOCUMENT_OWNER_MISMATCH";
    internal const string WorkbenchCommandTargetDocumentNotRegistered =
        "WORKBENCH_COMMAND_TARGET_DOCUMENT_NOT_REGISTERED";
    internal const string WorkbenchCommandPlacementIdOwnerMismatch =
        "WORKBENCH_COMMAND_PLACEMENT_ID_OWNER_MISMATCH";
    internal const string WorkbenchCommandPlacementCommandOwnerMismatch =
        "WORKBENCH_COMMAND_PLACEMENT_COMMAND_OWNER_MISMATCH";
    internal const string WorkbenchCommandPlacementCommandNotRegistered =
        "WORKBENCH_COMMAND_PLACEMENT_COMMAND_NOT_REGISTERED";
    internal const string WorkbenchMenuLocationUnsupported =
        "WORKBENCH_MENU_LOCATION_UNSUPPORTED";
    internal const string WorkbenchCommandIdDuplicate =
        "WORKBENCH_COMMAND_ID_DUPLICATE";
    internal const string HostCommandBindingMissing = "HOST_COMMAND_BINDING_MISSING";
    internal const string HostCommandBindingDuplicate = "HOST_COMMAND_BINDING_DUPLICATE";
    internal const string HostCommandBindingUnknown = "HOST_COMMAND_BINDING_UNKNOWN";
    internal const string WorkbenchCommandPlacementIdDuplicate =
        "WORKBENCH_COMMAND_PLACEMENT_ID_DUPLICATE";
    internal const string WorkbenchKeyGestureDuplicate =
        "WORKBENCH_KEY_GESTURE_DUPLICATE";
    internal const string WorkbenchKeyGestureConflict =
        "WORKBENCH_KEY_GESTURE_CONFLICT";
    internal const string PluginContainerBuildFailed = "PLUGIN_CONTAINER_BUILD_FAILED";
    internal const string HostContainerBuildFailed = "HOST_CONTAINER_BUILD_FAILED";
    internal const string ExtensionDiscoveryFailed = "EXTENSION_DISCOVERY_FAILED";
    internal const string ExtensionActivationFailed = "EXTENSION_ACTIVATION_FAILED";
    internal const string ToolAdapterActivationFailed = "TOOL_ADAPTER_ACTIVATION_FAILED";
    internal const string ToolLayoutOperationFailed = "TOOL_LAYOUT_OPERATION_FAILED";
    internal const string LifecycleInitializeFailed = "LIFECYCLE_INITIALIZE_FAILED";
    internal const string LifecycleInitializeTimeout = "LIFECYCLE_INITIALIZE_TIMEOUT";
    internal const string LifecycleShutdownFailed = "LIFECYCLE_SHUTDOWN_FAILED";
    internal const string LifecycleShutdownTimeout = "LIFECYCLE_SHUTDOWN_TIMEOUT";
    internal const string LifecycleHostCancelled = "LIFECYCLE_HOST_CANCELLED";
    internal const string LifecycleCancellationFailed = "LIFECYCLE_CANCELLATION_FAILED";
    internal const string LifecycleOperationRetained = "LIFECYCLE_OPERATION_RETAINED";
    internal const string LifecycleFailedShutdownRetained = "LIFECYCLE_FAILED_SHUTDOWN_RETAINED";
    internal const string LifecycleShutdownSkipped = "LIFECYCLE_SHUTDOWN_SKIPPED";
    internal const string LifecycleDrainCheckFailed = "LIFECYCLE_DRAIN_CHECK_FAILED";
    internal const string HostStartupCleanupFailed = "HOST_STARTUP_CLEANUP_FAILED";
    internal const string HostStartupUnexpected = "HOST_STARTUP_UNEXPECTED";
    internal const string WorkflowActionShutdownTimeout =
        "WORKFLOW_ACTION_SHUTDOWN_TIMEOUT";
    internal const string WorkbenchCommandExecutionFailed =
        "WORKBENCH_COMMAND_EXECUTION_FAILED";
    internal const string WorkbenchCommandTargetStateFailed =
        "WORKBENCH_COMMAND_TARGET_STATE_FAILED";
    internal const string WorkbenchCommandTargetSubscriptionFailed =
        "WORKBENCH_COMMAND_TARGET_SUBSCRIPTION_FAILED";
    internal const string WorkbenchCommandStateObserverFailed =
        "WORKBENCH_COMMAND_STATE_OBSERVER_FAILED";
    internal const string WorkbenchCommandDocumentCloseCancellationFailed =
        "WORKBENCH_COMMAND_DOCUMENT_CLOSE_CANCELLATION_FAILED";
    internal const string WorkbenchCommandShutdownTimeout =
        "WORKBENCH_COMMAND_SHUTDOWN_TIMEOUT";
}
