namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ConfiguredExportFactAttribute : FactAttribute
{
    public ConfiguredExportFactAttribute() : this(Environment.GetEnvironmentVariable) { }

    internal ConfiguredExportFactAttribute(Func<string, string?> readVariable)
    {
        if (new[] { "BRAIN_PLATFORM_EXPORT_RECORDING", "BRAIN_PLATFORM_EXPORT_OUTPUT", "BRAIN_PLATFORM_EXPORT_BACKEND" }
            .All(name => string.IsNullOrWhiteSpace(readVariable(name))))
            Skip = "Configure recording, output and backend to run real HTTP export acceptance.";
    }
}

public sealed class ConfiguredExportFactTests
{
    [Fact]
    public void Missing_configuration_is_skipped_during_discovery() =>
        Assert.NotNull(new ConfiguredExportFactAttribute(_ => null).Skip);

    [Theory]
    [InlineData("BRAIN_PLATFORM_EXPORT_RECORDING")]
    [InlineData("BRAIN_PLATFORM_EXPORT_OUTPUT")]
    [InlineData("BRAIN_PLATFORM_EXPORT_BACKEND")]
    public void Partial_configuration_runs_and_fails_required_input_checks(string configured) =>
        Assert.Null(new ConfiguredExportFactAttribute(name => name == configured ? "configured" : null).Skip);

    [Fact]
    public void Complete_configuration_runs_acceptance() =>
        Assert.Null(new ConfiguredExportFactAttribute(_ => "configured").Skip);
}
