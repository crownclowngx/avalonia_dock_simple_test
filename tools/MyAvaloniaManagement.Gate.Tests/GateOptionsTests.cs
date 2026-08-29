namespace MyAvaloniaManagement.Gate.Tests;

public sealed class GateOptionsTests
{
    [Fact]
    public void VerifyDefaultsToAll()
    {
        var options = GateOptions.Parse(["verify"]);

        Assert.Equal(GateProfile.Verify, options.Profile);
        Assert.Equal(GateScope.All, options.Scope);
        Assert.False(options.Repeat);
    }

    [Fact]
    public void VerifyAcceptsDiagnosticScopeAndRepositoryOverrides()
    {
        var options = GateOptions.Parse([
            "verify", "--scope", "workflow", "--workflow-studio", "C:/studio"]);

        Assert.Equal(GateScope.Workflow, options.Scope);
        Assert.Equal("C:/studio", options.WorkflowStudioRoot);
        Assert.True(options.Includes("workflow"));
        Assert.False(options.Includes("workbench"));
    }

    [Fact]
    public void SealAcceptsRepeat()
    {
        var options = GateOptions.Parse(["seal", "--repeat"]);

        Assert.Equal(GateProfile.Seal, options.Profile);
        Assert.True(options.Repeat);
    }

    [Fact]
    public void WorkbenchIncludesItsHostAndWorkflowDependencies()
    {
        var options = GateOptions.Parse(["verify", "--scope", "workbench"]);

        Assert.True(options.Includes("host"));
        Assert.True(options.Includes("workflow"));
        Assert.True(options.Includes("workbench"));
    }

    [Theory]
    [InlineData("verify", "--repeat")]
    [InlineData("seal", "--scope", "host")]
    [InlineData("unknown")]
    public void InvalidCombinationsAreRejected(params string[] arguments)
    {
        Assert.Throws<GateUsageException>(() => GateOptions.Parse(arguments));
    }
}
