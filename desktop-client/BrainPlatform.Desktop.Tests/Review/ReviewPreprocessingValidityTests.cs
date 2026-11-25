using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task ReconstructionGap_ShowsInputDependencyWithoutAddingArtifactMarkers()
    {
        using var vm = CreateViewModel(new GapPreviewClient(), out _);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.Contains("重建不可用", vm.OverlayStatusText);
        Assert.Contains("非新增坏段", vm.OverlayStatusText);
        Assert.Contains("Fp1", vm.OverlayStatusText);
        Assert.Contains("另 1 路不可用", vm.OverlayStatusText);
        Assert.Empty(vm.ArtifactMarkers);
        var validity = vm.CurrentOverlayWindow!.Processed.ArtifactRemovalValidity!;
        Assert.Equal(50, validity.AdditionalUnavailableChannelSamples);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("range")]
    [InlineData("channel")]
    public void ReconstructionGap_InvalidResponseIsRejected(string invalid)
    {
        var gap = new ArtifactRemovalUnavailableInterval(100, 150, .2, .3, ["Fp1"], ["Fp1", "C4"], ["C4"], "missing_input_dependency");
        var validity = new ArtifactRemovalValidity("artifact-removal-validity-v1", 500, 0, 5000,
            ["Fp1", "C4"], 50, 100, 50, [gap]);
        var bad = invalid switch
        {
            "count" => validity with { AdditionalUnavailableChannelSamples = 49 },
            "range" => validity with { Intervals = [gap with { EndSample = 6000 }] },
            _ => validity with { Intervals = [gap with { UnavailableOutputChannels = ["unknown"] }] },
        };
        Assert.Throws<InvalidOperationException>(() => bad.Validate(0, 10, 500, ["Fp1", "C4"]));
    }

    private sealed class GapPreviewClient : FakePreprocessingClient
    {
        public override async Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            var preview = await base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
            var start = (long)(request.StartSeconds * 500);
            var gap = new ArtifactRemovalUnavailableInterval(start + 100, start + 150,
                request.StartSeconds + .2, request.StartSeconds + .3, ["Fp1"], ["Fp1", "C4"], ["C4"], "missing_input_dependency");
            return preview with
            {
                Processed = preview.Processed with
                {
                    ArtifactRemovalValidity = new ArtifactRemovalValidity(
                        "artifact-removal-validity-v1", 500, start, (long)(preview.EndSeconds * 500),
                        preview.Processed.Channels, 50, 100, 50, [gap]),
                },
            };
        }
    }
}
