using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task PairedPreview_BudgetFromPlotWidthSubSecondWindowAndObsoleteWidthDiscard()
    {
        var client = new BudgetCapturingClient();
        using var vm = new ReviewPreprocessingViewModel(
            _ => Task.FromResult<(string, string?, double)>(("rec-1", "montage-avg", 60.0)),
            () => client,
            _ => { },
            () => (10.0, 0.25, 1.0, 30.0, 50.0));

        // Width not ready -> no HTTP request issued.
        vm.UpdatePlotWidth(0);
        Assert.Equal(0, vm.PreviewPointBudget);
        vm.IsOverlayCompareEnabled = true;
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(0, client.RequestCount);
        Assert.False(vm.IsScientificComparisonReady);

        // Plot width 900 -> budget 1800; sub-second 0.25s window preserved without clamping to 1s.
        vm.UpdatePlotWidth(900);
        Assert.Equal(1800, vm.PreviewPointBudget);
        await vm.RefreshOverlayWindowAsync();
        Assert.NotNull(vm.CurrentOverlayWindow);
        Assert.True(vm.IsScientificComparisonReady);
        Assert.Equal(0.25, client.LastRequest!.WindowSeconds, precision: 4);
        Assert.Equal(1800, client.LastRequest.MaxPoints);
        Assert.Equal(30.0, client.LastRequest.NotchQ);

        // Same budget -> no redundant request.
        var countBefore = client.RequestCount;
        vm.UpdatePlotWidth(900);
        Assert.Equal(countBefore, client.RequestCount);

        // Upper clamp 4000, lower clamp 1000.
        Assert.Equal(1000, ReviewPreprocessingViewModel.ComputePointBudget(200));
        Assert.Equal(4000, ReviewPreprocessingViewModel.ComputePointBudget(3000));

        // Confirmed ICA survives display density / comparison toggle changes.
        var ica = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], ica.IcaFitFingerprint);
        vm.UpdatePlotWidth(1400);
        await vm.RefreshOverlayWindowAsync();
        Assert.Equal(ica.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.Equal(new[] { 0 }, vm.ConfirmedIcaExcluded);
    }

    [Fact]
    public void PairedPreviewContract_RejectsObsoleteSchemaFilterMismatchAndDisabledDivergence()
    {
        var times = new Dictionary<string, IReadOnlyList<double>> { ["Fp1"] = [0.0, 0.5, 0.998] };
        var baseTraces = new Dictionary<string, IReadOnlyList<double?>> { ["Fp1"] = [1.0, 2.0, 3.0] };
        var diffTraces = new Dictionary<string, IReadOnlyList<double?>> { ["Fp1"] = [1.0, 9.0, 3.0] };
        var valid = new PreprocessingPreviewWindowResponse(
            SchemaVersion: PreprocessingPreviewWindowResponse.CurrentSchemaVersion,
            RecordingId: "rec-1",
            SourceSha256: "sha",
            MontageId: "m1",
            MontageSnapshotSha256: "msha",
            StartSample: 0,
            EndSample: 500,
            StartSeconds: 0.0,
            EndSeconds: 1.0,
            SamplingRateHz: 500.0,
            Unit: "µV",
            CommonFilter: new PreviewFilterEvidence("whole_recording_before_window_slice", "zero_phase", [1.0, 50.0], 50.0, 30.0, "fp1"),
            EnhancedPreprocessingActive: false,
            DisplayCompression: new PreviewDisplayCompression(PreprocessingPreviewWindowResponse.PairedCompressionMethod, false, 1200, true),
            Baseline: new PreviewBaselineSide("scientific_baseline", ["Fp1"], times, baseTraces),
            Processed: new PreviewProcessedSide("scientific_processed", ["Fp1"], [], [], [], times, baseTraces, false, "skipped", null));

        valid.Validate();
        Assert.Throws<InvalidOperationException>(() => (valid with { SchemaVersion = "legacy-v0" }).Validate());
        Assert.Throws<InvalidOperationException>(() => (valid with { Unit = "V" }).Validate());
        Assert.Throws<InvalidOperationException>(() => (valid with
        {
            CommonFilter = valid.CommonFilter with { FilterPhase = "causal" },
        }).Validate());
        Assert.Throws<InvalidOperationException>(() => (valid with
        {
            Processed = valid.Processed with { TracesUv = diffTraces },
        }).Validate());
    }

    [Fact]
    public void PairedOverlayProjection_DrawsOnceWhenDisabledAndKeepsExcludedRowAlignedWithoutShifting()
    {
        var times = new Dictionary<string, IReadOnlyList<double>>
        {
            ["Fp1"] = [1.0, 1.5, 2.0],
            ["C4"] = [1.0, 1.5, 2.0],
            ["O1"] = [1.0, 1.5, 2.0],
        };
        var baseTraces = new Dictionary<string, IReadOnlyList<double?>>
        {
            ["Fp1"] = [10.0, -10.0, 5.0],
            ["C4"] = [20.0, 0.0, -20.0],
            ["O1"] = [30.0, null, 15.0],
        };
        var disabled = new PreprocessingPreviewWindowResponse(
            PreprocessingPreviewWindowResponse.CurrentSchemaVersion, "rec-1", "sha", "m1", "msha",
            500, 1000, 1.0, 2.0, 500.0, "µV",
            new PreviewFilterEvidence("whole_recording_before_window_slice", "zero_phase", [1.0, 50.0], 50.0, 30.0, "fp1"),
            EnhancedPreprocessingActive: false,
            new PreviewDisplayCompression(PreprocessingPreviewWindowResponse.PairedCompressionMethod, false, 1200, true),
            new PreviewBaselineSide("scientific_baseline", ["Fp1", "C4", "O1"], times, baseTraces),
            new PreviewProcessedSide("scientific_processed", ["Fp1", "C4", "O1"], [], [], [], times, baseTraces, false, "skipped", null));

        // When all enhanced policies are off, DrawBaseline=false and DrawProcessed=true (single draw, no thickening!).
        var row0 = ReviewPreprocessingOverlayProjection.ProjectRow(disabled, "Fp1", 0, 3, 0.01);
        Assert.False(row0.DrawBaseline);
        Assert.True(row0.DrawProcessed);
        Assert.Equal([1.0, 1.5, 2.0], row0.ProcessedX!);
        Assert.Equal(2.5 + 10.0 * 0.01, row0.ProcessedY![0], precision: 6);

        // NaN gap preserved explicitly.
        var row2 = ReviewPreprocessingOverlayProjection.ProjectRow(disabled, "O1", 2, 3, 0.01);
        Assert.True(double.IsNaN(row2.ProcessedY![1]));

        // When C4 is excluded on processed side, C4 row draws baseline only, O1 stays on row 2 without shifting!
        var procTimes = new Dictionary<string, IReadOnlyList<double>> { ["Fp1"] = times["Fp1"], ["O1"] = times["O1"] };
        var procTraces = new Dictionary<string, IReadOnlyList<double?>> { ["Fp1"] = [9.0, -9.0, 4.0], ["O1"] = [28.0, null, 14.0] };
        var excluded = disabled with
        {
            EnhancedPreprocessingActive = true,
            Processed = new PreviewProcessedSide(
                "scientific_processed", ["Fp1", "O1"], ["C4"], ["Fp1"], ["C4"], procTimes, procTraces, false, "skipped", null),
        };
        excluded.Validate();
        var fp1Row = ReviewPreprocessingOverlayProjection.ProjectRow(excluded, "Fp1", 0, 3, 0.01);
        var c4Row = ReviewPreprocessingOverlayProjection.ProjectRow(excluded, "C4", 1, 3, 0.01);
        var o1Row = ReviewPreprocessingOverlayProjection.ProjectRow(excluded, "O1", 2, 3, 0.01);

        Assert.True(fp1Row.DrawBaseline && fp1Row.DrawProcessed && fp1Row.IsInterpolated);
        Assert.True(c4Row.DrawBaseline && !c4Row.DrawProcessed);
        Assert.True(o1Row.DrawBaseline && o1Row.DrawProcessed);
        Assert.Equal(0.5 + 28.0 * 0.01, o1Row.ProcessedY![0], precision: 6);
    }

    private sealed class BudgetCapturingClient : FakePreprocessingClient
    {
        public int RequestCount { get; private set; }
        public PreprocessingPreviewWindowRequest? LastRequest { get; private set; }

        public override Task<PreprocessingPreviewWindowResponse> PreviewPreprocessedWindowAsync(
            string recordingId, PreprocessingPreviewWindowRequest request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            return base.PreviewPreprocessedWindowAsync(recordingId, request, cancellationToken);
        }
    }
}
