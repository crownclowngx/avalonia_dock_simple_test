namespace MyAvaloniaManagement.Gate.Tests;

public sealed class GateOptionsTests
{
    [Fact]
    public void VerifyDefaultsToCompleteLocalScope()
    {
        var options = GateOptions.Parse(["verify"]);
        Assert.Equal(GateProfile.Verify, options.Profile);
        Assert.Equal(GateScope.All, options.Scope);
        Assert.False(options.Repeat);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("all")]
    public void VerifyAcceptsLocalAliases(string scope)
    {
        Assert.Equal(scope, GateOptions.Parse(["verify", "--scope", scope]).Scope.ToString().ToLowerInvariant());
    }

    [Fact]
    public void SealAcceptsRepeat()
    {
        var options = GateOptions.Parse(["seal", "--repeat"]);
        Assert.Equal(GateProfile.Seal, options.Profile);
        Assert.True(options.Repeat);
    }

    [Theory]
    [InlineData("verify", "--repeat")]
    [InlineData("seal", "--scope", "host")]
    [InlineData("verify", "--scope")]
    [InlineData("unknown")]
    public void InvalidCombinationsAreRejected(params string[] arguments)
    {
        Assert.Throws<GateUsageException>(() => GateOptions.Parse(arguments));
    }

    [Theory]
    [InlineData("--scope", "workflow")]
    [InlineData("--scope", "workbench")]
    [InlineData("--workflow-studio", "C:/studio")]
    [InlineData("--classic-game", "C:/game")]
    public void RetiredExternalOptionsExplainLocalScope(string option, string value)
    {
        var failure = Assert.Throws<GateUsageException>(() => GateOptions.Parse(["verify", option, value]));
        Assert.Contains("已退役", failure.Message);
    }
}
