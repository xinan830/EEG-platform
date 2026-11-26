namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Theory]
    [InlineData("ptp")]
    [InlineData("muscle")]
    [InlineData("bad-policy")]
    [InlineData("bad-selection")]
    [InlineData("montage")]
    public async Task DetectorInputChange_ClearsStaleArtifactRowsAndMarkers(string change)
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        Assert.NotEmpty(vm.ArtifactSegments);
        switch (change)
        {
            case "ptp": vm.PeakToPeakUv += 50; break;
            case "muscle": vm.MuscleZScoreThreshold += 1; break;
            case "bad-policy": vm.BadChannelPolicy = "exclude"; break;
            case "bad-selection": vm.BadChannelRows.Single(r => r.Channel == "C4").IsSelected = false; break;
            case "montage": vm.InvalidateSignalInputs(); break;
        }
        Assert.Empty(vm.ArtifactSegments);
        Assert.Empty(vm.ArtifactMarkers);
        Assert.Contains("重新扫描", vm.CleanRatioSummary);
    }

    [Fact]
    public async Task ArtifactTreatmentPreservesReport_ButRemovalMethodInvalidatesIt()
    {
        using var vm = CreateViewModel(new FakePreprocessingClient(), out _);
        await vm.InspectArtifactSegmentsAsync();
        var rows = vm.ArtifactSegments.ToArray();
        vm.ArtifactPolicy = "annotate";
        Assert.Equal(rows, vm.ArtifactSegments.ToArray());
        Assert.NotEmpty(vm.ArtifactMarkers);
        vm.RemovalMethod = "ssp_eog";
        Assert.Empty(vm.ArtifactSegments);
        Assert.Empty(vm.ArtifactMarkers);
    }
}
