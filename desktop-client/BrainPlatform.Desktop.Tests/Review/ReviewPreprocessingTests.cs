using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task BadChannelInspection_ResolvesCandidateInterpolatedAndExcludedBadgesAcrossMontageSuffixes()
    {
        var fakeClient = new FakePreprocessingClient();
        var vm = CreateViewModel(fakeClient, out _);

        await vm.InspectBadChannelsAsync();

        Assert.Equal(2, vm.BadChannelRows.Count);
        Assert.Equal("interpolate", vm.BadChannelPolicy);
        Assert.Equal(["C4"], vm.SelectedBadChannels);

        var interpBadge = vm.ResolveChannelBadge("C4-AVG");
        Assert.Equal(ChannelVisualPreprocessingState.CandidateBad, interpBadge.State);
        Assert.Contains("待预览验证", interpBadge.Tooltip);
        Assert.Contains("高频噪声", interpBadge.Tooltip);

        vm.BadChannelPolicy = "exclude";
        var excludedBadge = vm.ResolveChannelBadge("C4-REF");
        Assert.Equal(ChannelVisualPreprocessingState.CandidateBad, excludedBadge.State);
        Assert.Contains("待预览验证", excludedBadge.Tooltip);

        vm.BadChannelPolicy = "none";
        var candidateBadge = vm.ResolveChannelBadge("C4");
        Assert.Equal(ChannelVisualPreprocessingState.CandidateBad, candidateBadge.State);

        var normalBadge = vm.ResolveChannelBadge("O1-AVG");
        Assert.Equal(ChannelVisualPreprocessingState.Normal, normalBadge.State);
    }

    [Fact]
    public async Task ArtifactSegmentInspection_PopulatesMarkersSupportsJumpAndToggleVisibility()
    {
        var fakeClient = new FakePreprocessingClient();
        var vm = CreateViewModel(fakeClient, out var seekTargets);

        await vm.InspectArtifactSegmentsAsync();

        Assert.Equal(2, vm.ArtifactSegments.Count);
        Assert.Equal(2, vm.ArtifactMarkers.Count);
        Assert.Equal("reject", vm.ArtifactPolicy);
        Assert.Contains("95.0%", vm.CleanRatioSummary);

        vm.JumpToSegmentCommand.Execute(vm.ArtifactSegments[1]);
        Assert.Single(seekTargets);
        Assert.Equal(19.5, seekTargets[0], precision: 3);

        vm.ShowArtifactBandsOnWaveform = false;
        Assert.Empty(vm.ArtifactMarkers);

        vm.ShowArtifactBandsOnWaveform = true;
        Assert.Equal(2, vm.ArtifactMarkers.Count);
    }

    [Fact]
    public async Task IcaConfirmation_LocksFingerprintBuildsContinuousConfigOmitsBoundedRemovalAndInvalidatesOnUpstreamChange()
    {
        var fakeClient = new FakePreprocessingClient();
        var vm = CreateViewModel(fakeClient, out _);

        await vm.InspectBadChannelsAsync();
        await vm.InspectArtifactSegmentsAsync();
        var response = await vm.InspectIcaForDialogAsync();

        Assert.Equal(2, vm.IcaComponents.Count);
        Assert.True(vm.IcaComponents[0].IsExcluded);
        Assert.False(vm.IcaComponents[1].IsExcluded);
        Assert.Equal("解释方差 24.0%", vm.IcaComponents[0].EvrText);
        Assert.NotNull(vm.IcaComponents[0].Topomap);
        Assert.NotEmpty(vm.IcaComponents[0].PreviewWaveform);

        vm.ConfirmIcaSelection([0], response.IcaFitFingerprint);
        Assert.Equal("ica_infomax", vm.RemovalMethod);
        Assert.Equal("fp_abc123456789", vm.IcaFitFingerprint);

        var continuousConfig = vm.BuildAlgorithmConfigDictionary(allowArtifactRemoval: true);
        Assert.NotNull(continuousConfig);
        var removalDict = Assert.IsType<Dictionary<string, object?>>(continuousConfig!["artifact_removal"]);
        Assert.Equal("ica_infomax", removalDict["method"]);
        Assert.Equal("fp_abc123456789", removalDict["ica_fit_fingerprint"]);
        Assert.Equal(new[] { 0 }, removalDict["ica_exclude_components"]);

        var boundedConfig = vm.BuildAlgorithmConfigDictionary(allowArtifactRemoval: false);
        Assert.NotNull(boundedConfig);
        var boundedRemoval = Assert.IsType<Dictionary<string, object?>>(boundedConfig!["artifact_removal"]);
        Assert.Equal("none", boundedRemoval["method"]);
        var boundedBad = Assert.IsType<Dictionary<string, object?>>(boundedConfig["bad_channels"]);
        Assert.Equal("interpolate", boundedBad["policy"]);

        // Changing upstream bad-channel policy must invalidate locked ICA fingerprint.
        vm.BadChannelPolicy = "exclude";
        Assert.Null(vm.IcaFitFingerprint);
        var afterInvalidation = vm.BuildAlgorithmConfigDictionary(allowArtifactRemoval: true);
        var removalAfter = Assert.IsType<Dictionary<string, object?>>(afterInvalidation!["artifact_removal"]);
        Assert.Equal("none", removalAfter["method"]);
    }

    [Fact]
    public async Task OverlayCompare_FetchesCleanedPreviewWindowAndClearsOnDisable()
    {
        var fakeClient = new FakePreprocessingClient();
        var vm = CreateViewModel(fakeClient, out _);

        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();

        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.True(vm.CurrentOverlayWindow!.IcaModelReused);
        Assert.Contains("复用已审查 ICA 模型", vm.OverlayStatusText);

        vm.IsOverlayCompareEnabled = false;
        Assert.Null(vm.CurrentOverlayWindow);
    }

    private static ReviewPreprocessingViewModel CreateViewModel(
        IPreprocessingClient client,
        out List<double> seekTargets)
    {
        var targets = new List<double>();
        seekTargets = targets;
        return new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60.0)),
            () => client,
            sec => targets.Add(sec),
            () => (10.0, 10.0, 1.0, 30.0, 50.0));
    }

    private class FakePreprocessingClient : IPreprocessingClient
    {
        public virtual Task<BadChannelInspectionResponse> InspectBadChannelsAsync(
            string recordingId, double startSeconds, double windowSeconds, CancellationToken cancellationToken) =>
            Task.FromResult(new BadChannelInspectionResponse(
                RecordingId: recordingId,
                SamplingRateHz: 500,
                SampleCount: 30000,
                StartSeconds: startSeconds,
                EndSeconds: startSeconds + windowSeconds,
                MontageTemplate: "standard_1020",
                RecommendedBadChannels: ["C4"],
                Channels:
                [
                    new BadChannelInspectionItem("C4", true, true, ["high_freq_noise"], 0.0, 85.2, 420.0, 210.0, 80.0, 0.55, 0.12, true, null),
                    new BadChannelInspectionItem("O1", true, false, [], 0.0, 18.4, 75.0, 42.0, 17.5, 0.04, 0.81, true, null),
                ]));

        public virtual Task<ArtifactSegmentInspectionResponse> InspectArtifactSegmentsAsync(
            string recordingId, double startSeconds, double windowSeconds, string? montageId,
            double? peakToPeakUv, double? muscleZScoreThreshold, IReadOnlyList<string>? badChannels,
            string badChannelPolicy, CancellationToken cancellationToken) =>
            Task.FromResult(new ArtifactSegmentInspectionResponse(
                RecordingId: recordingId,
                SamplingRateHz: 500,
                SampleCount: 30000,
                StartSeconds: startSeconds,
                EndSeconds: startSeconds + windowSeconds,
                CleanRatio: 0.95,
                RejectedSampleCount: 1500,
                MuscleCheckStatus: "mne_standard_110_140_hz",
                MuscleBandHz: [110.0, 140.0],
                Segments:
                [
                    new ArtifactSegmentItem(2500, 2750, 5.0, 5.5, 0.5, "amplitude_ptp", ["Fp1"], 475.0),
                    new ArtifactSegmentItem(10000, 10160, 20.0, 20.32, 0.32, "muscle_zscore", ["T7", "T8"], 5.2),
                ]));

        public virtual Task<IcaInspectionResponse> InspectIcaComponentsAsync(
            string recordingId, string? montageId, double lowHz, double highHz, double? notchHz,
            IReadOnlyList<string>? badChannels, string badChannelPolicy, string artifactPolicy,
            double? peakToPeakUv, double? muscleZScoreThreshold, bool allowFrontalEogProxy,
            int? icaComponentCount, double icaEogThreshold, CancellationToken cancellationToken)
        {
            var grid = new TopomapGrid(
                Method: "idw",
                Width: 3,
                Height: 3,
                XMin: -1,
                XMax: 1,
                YMin: -1,
                YMax: 1,
                HeadRadius: 1.0,
                Values:
                [
                    [0.0, 0.5, 0.0],
                    [0.5, 1.0, 0.5],
                    [0.0, 0.5, 0.0],
                ]);
            var topomap = new TopomapField(
                FieldId: "ica.ic00",
                Label: "IC00",
                Unit: "a.u.",
                FrequencyHz: null,
                Points: [new TopomapPoint("Fp1", "Fp1", -0.2, 0.7, 1.0, "included", null)],
                Grid: grid,
                Status: "ok",
                IncludedChannels: ["Fp1"],
                ExcludedChannels: [],
                Failure: null,
                Provenance: null);
            return Task.FromResult(new IcaInspectionResponse(
                RecordingId: recordingId,
                Method: "ica_infomax",
                SamplingRateHz: 500,
                SampleCount: 30000,
                ComponentCount: 2,
                EogChannelsUsed: ["Fp1", "Fp2"],
                EogChannelSource: "frontal_eeg_proxy",
                EcgChannelsUsed: [],
                RecommendedExcludeComponents: [0],
                SelectedExcludeComponents: [0],
                IcaFitFingerprint: "fp_abc123456789",
                IcaCacheHit: false,
                CanApplyToContinuous: true,
                Components:
                [
                    new IcaComponentItem(0, 0.91, null, 0.24, true, true, topomap, [0.0, 0.5, -0.4, 0.8], 10.0,
                        [0, 0.17, 0.9, 9.998], "z-score", new PreviewDisplayCompression("finite_run_min_max_envelope_v1", false, 240, true)),
                    new IcaComponentItem(1, 0.12, null, 0.11, false, false, topomap, [0.1, -0.1, 0.05, -0.05], 10.0,
                        [0, 0.2, 0.95, 9.998], "z-score", new PreviewDisplayCompression("finite_run_min_max_envelope_v1", false, 240, true)),
                ]));
        }

        public virtual Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PreprocessingPreviewWindowResponse(
                RecordingId: recordingId,
                MontageId: request.MontageId,
                StartSeconds: request.StartSeconds,
                EndSeconds: request.StartSeconds + request.WindowSeconds,
                SamplingRateHz: 500,
                Channels: ["Fp1", "C4"],
                InterpolatedChannels: ["C4"],
                ExcludedChannels: [],
                TraceTimesSeconds: new Dictionary<string, IReadOnlyList<double>>
                {
                    ["Fp1"] = [request.StartSeconds, request.StartSeconds + 0.5, request.StartSeconds + 1],
                    ["C4"] = [request.StartSeconds, request.StartSeconds + 0.4, request.StartSeconds + 1],
                },
                TracesUv: new Dictionary<string, IReadOnlyList<double?>>
                {
                    ["Fp1"] = [10.0, -5.0, 8.0],
                    ["C4"] = [4.0, 2.0, -3.0],
                },
                IcaModelReused: true,
                ArtifactRemovalStatus: "applied",
                DisplayCompression: new PreviewDisplayCompression("finite_run_min_max_envelope_v1", false, 1000, true)));
    }
}
