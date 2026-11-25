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
        Assert.False(vm.CurrentOverlayWindow!.EnhancedPreprocessingActive);
        Assert.Contains("增强处理全关", vm.OverlayStatusText);
        Assert.True(vm.IsScientificComparisonReady);

        var ica = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], ica.IcaFitFingerprint);
        await vm.RefreshOverlayWindowAsync();

        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.True(vm.CurrentOverlayWindow!.EnhancedPreprocessingActive);
        Assert.True(vm.CurrentOverlayWindow!.Processed.IcaModelReused);
        Assert.Contains("复用已审查 ICA 模型", vm.OverlayStatusText);

        vm.IsOverlayCompareEnabled = false;
        Assert.Null(vm.CurrentOverlayWindow);
        Assert.False(vm.IsScientificComparisonReady);
    }

    [Fact]
    public async Task PipelineFlowchart_UpdatesNodeStatesBadgesAndMetadataSummaries()
    {
        var fakeClient = new FakePreprocessingClient();
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 300.0)),
            () => fakeClient,
            _ => { },
            () => (0, 10, 1, 30, 50),
            () => new PreprocessingRecordingContext("受试者01", 32, 500, 300.0, "平均参考 (CAR)", 1.0, 30.0, 50.0));

        Assert.Equal("32 导 · 500 Hz · 05:00", vm.InputNodeSummary);
        Assert.Equal("平均参考 (CAR) · 零相位带通/陷波", vm.MontageNodeSummary);
        Assert.Equal("inactive", vm.BadChannelNodeState);
        Assert.Equal("跳过", vm.BadChannelNodeBadge);
        Assert.Equal("inactive", vm.ArtifactNodeState);
        Assert.Equal("inactive", vm.RemovalNodeState);

        await vm.InspectBadChannelsAsync();
        Assert.Equal("active", vm.BadChannelNodeState);
        Assert.Equal("3D 插值 · 1 导", vm.BadChannelNodeBadge);
        Assert.True(vm.IsBadChannelPolicyInterpolate);

        vm.IsBadChannelPolicyExclude = true;
        Assert.Equal("exclude", vm.BadChannelPolicy);
        Assert.Equal("CAR 排除 · 1 导", vm.BadChannelNodeBadge);

        await vm.InspectArtifactSegmentsAsync();
        Assert.Equal("active", vm.ArtifactNodeState);
        Assert.Equal("掩膜排除 · 2 段", vm.ArtifactNodeBadge);
        Assert.True(vm.IsArtifactPolicyReject);

        vm.IsRemovalIca = true;
        Assert.Equal("warning", vm.RemovalNodeState);
        Assert.Equal("待拟合审查", vm.RemovalNodeBadge);

        var ica = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], ica.IcaFitFingerprint);
        Assert.Equal("active", vm.RemovalNodeState);
        Assert.Equal("ICA 剔除 · 1 成分", vm.RemovalNodeBadge);
        Assert.Contains("坏导:1(排除)", vm.RibbonToolTipText);

        vm.ResetAll();
        Assert.Equal("inactive", vm.BadChannelNodeState);
        Assert.Equal("inactive", vm.ArtifactNodeState);
        Assert.Equal("inactive", vm.RemovalNodeState);
    }

    [Fact]
    public async Task AnalysisSnapshot_BindsBackendNormalizedPreprocessingDefaultsAndRejectsTamperedValues()
    {
        var fakeClient = new FakePreprocessingClient();
        using var vm = CreateViewModel(fakeClient, out _);
        await vm.InspectArtifactSegmentsAsync();
        var ica = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], ica.IcaFitFingerprint);

        var prep = vm.BuildAlgorithmConfigDictionary(allowArtifactRemoval: true);
        var requestConfig = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["algorithm_id"] = "rbp",
            ["mode"] = "dynamic",
            ["channel"] = "Fp1",
            ["time"] = new Dictionary<string, object?> { ["start_s"] = 0.0, ["end_s"] = 60.0 },
            ["preprocessing"] = prep,
        });
        var recording = new RegisteredRecording(
            "rec-1", "demo", ".edf", "2026-11-25T00:00:00Z", 500, 60.0, ["Fp1", "C4"], "abc123");
        var snapshot = BrainPlatform.Desktop.Modules.Review.Analysis.ReviewAnalysisSnapshot.From(
            new AnalysisRunRequest("rec-1", "official_algorithm", requestConfig), recording, 0);

        // Simulate backend OfficialAlgorithmRunConfig.model_dump(mode="json") with additional server defaults.
        var canonical = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["scientific_configuration_contract"] = "scientific-config-v1",
            ["config"] = new Dictionary<string, object?>
            {
                ["algorithm_id"] = "rbp",
                ["scientific_version"] = "2.0.8",
                ["mode"] = "dynamic",
                ["channel"] = "Fp1",
                ["dynamic_window_s"] = 10.0,
                ["refresh_step_s"] = 1.0,
                ["time"] = new Dictionary<string, object?> { ["start_s"] = 0.0, ["end_s"] = 60.0 },
                ["preprocessing"] = new Dictionary<string, object?>
                {
                    ["bad_channels"] = new Dictionary<string, object?> { ["policy"] = "none", ["channels"] = Array.Empty<string>() },
                    ["artifact_rejection"] = new Dictionary<string, object?>
                    {
                        ["policy"] = "reject",
                        ["peak_to_peak_uv"] = 300.0,
                        ["amplitude_window_s"] = 0.5,
                        ["muscle_zscore_threshold"] = 4.0,
                        ["min_duration_s"] = 0.1,
                        ["pad_s"] = 0.1,
                        ["manual_segments"] = Array.Empty<object>(),
                    },
                    ["artifact_removal"] = new Dictionary<string, object?>
                    {
                        ["method"] = "ica_infomax",
                        ["eog_channels"] = Array.Empty<string>(),
                        ["ecg_channels"] = Array.Empty<string>(),
                        ["allow_frontal_eog_proxy"] = true,
                        ["ica_n_components"] = 15,
                        ["ica_eog_threshold"] = 0.7,
                        ["ica_exclude_components"] = new[] { 0 },
                        ["ica_fit_fingerprint"] = "fp_abc123456789",
                        ["ssp_n_eeg"] = 1,
                        ["random_state"] = 42,
                        ["future_server_default"] = "ok",
                    },
                },
            },
            ["context"] = new Dictionary<string, object?> { ["sampling_rate_hz"] = 500.0 },
        });
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        var bound = snapshot.BindScientificConfiguration(canonical, sha);
        Assert.NotNull(bound.ScientificConfiguration);

        // Tampering with an explicitly requested preprocessing value must still be rejected.
        var tampered = canonical.Replace("fp_abc123456789", "fp_wrong");
        var tamperedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(tampered))).ToLowerInvariant();
        Assert.Throws<InvalidOperationException>(() => snapshot.BindScientificConfiguration(tampered, tamperedSha));
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
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            var sfreq = 500.0;
            var startSample = (long)Math.Floor(request.StartSeconds * sfreq);
            var endSample = startSample + Math.Max(1L, (long)Math.Round(request.WindowSeconds * sfreq));
            var startS = Math.Round(startSample / sfreq, 6);
            var endS = Math.Round(endSample / sfreq, 6);
            var midS = Math.Round((startS + endS) / 2.0, 6);
            var lastS = Math.Round((endSample - 1) / sfreq, 6);
            var enhanced = request.Preprocessing.BadChannels.Policy != "none" ||
                           request.Preprocessing.ArtifactRejection.Policy != "none" ||
                           request.Preprocessing.ArtifactRemoval.Method != "none";
            var times = new Dictionary<string, IReadOnlyList<double>>
            {
                ["Fp1"] = [startS, midS, lastS],
                ["C4"] = [startS, midS, lastS],
            };
            var baseTraces = new Dictionary<string, IReadOnlyList<double?>>
            {
                ["Fp1"] = [10.0, -5.0, 8.0],
                ["C4"] = [4.0, 2.0, -3.0],
            };
            var procTraces = enhanced
                ? new Dictionary<string, IReadOnlyList<double?>>
                {
                    ["Fp1"] = [9.5, -4.5, 7.5],
                    ["C4"] = [3.5, 1.8, -2.5],
                }
                : baseTraces;
            return Task.FromResult(new PreprocessingPreviewWindowResponse(
                SchemaVersion: PreprocessingPreviewWindowResponse.CurrentSchemaVersion,
                RecordingId: recordingId,
                SourceSha256: "source-sha",
                MontageId: request.MontageId,
                MontageSnapshotSha256: "montage-sha",
                StartSample: startSample,
                EndSample: endSample,
                StartSeconds: startS,
                EndSeconds: endS,
                SamplingRateHz: sfreq,
                Unit: "µV",
                CommonFilter: new PreviewFilterEvidence(
                    "whole_recording_before_window_slice", "zero_phase",
                    [request.LowHz, request.HighHz], request.NotchHz, request.NotchQ, "fp_filter_1_50"),
                EnhancedPreprocessingActive: enhanced,
                DisplayCompression: new PreviewDisplayCompression(
                    PreprocessingPreviewWindowResponse.PairedCompressionMethod, false, request.MaxPoints, true),
                Baseline: new PreviewBaselineSide("scientific_baseline", ["Fp1", "C4"], times, baseTraces),
                Processed: new PreviewProcessedSide(
                    Role: "scientific_processed",
                    Channels: ["Fp1", "C4"],
                    ExcludedOutputChannels: [],
                    InterpolatedChannels: ["C4"],
                    ExcludedChannels: [],
                    TraceTimesSeconds: times,
                    TracesUv: procTraces,
                    IcaModelReused: true,
                    ArtifactRemovalStatus: enhanced ? "applied" : "skipped",
                    ArtifactRemovalValidity: null)));
        }
    }
}
