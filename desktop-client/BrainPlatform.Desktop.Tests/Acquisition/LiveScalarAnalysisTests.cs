using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Acquisition.Views;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;
using BrainPlatform.Desktop.Tests.Review;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveScalarAnalysisTests
{
    [Fact]
    public void Six_live_cards_open_in_parallel_and_theta_beta_mode_reconfigures() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeScalarBackend(sessionId, ["Fz", "Pz", "O1"]);
            Exception? reportedError = null;
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, ex => reportedError = ex);

            await workspace.OpenOrFocusAsync("rbp");
            await workspace.OpenOrFocusAsync("psd");
            await workspace.OpenOrFocusAsync("stft");
            await workspace.OpenOrFocusAsync("iapf");
            await workspace.OpenOrFocusAsync("theta_beta");
            await workspace.OpenOrFocusAsync("brainbeat");
            FlushDispatcher(dispatcher);

            Assert.Null(reportedError);
            Assert.Equal(6, workspace.RightDockedSlots.Count);
            Assert.True(workspace.IsIapfOpen);
            Assert.True(workspace.IsThetaBetaOpen);
            Assert.True(workspace.IsBrainbeatOpen);

            var brainbeat = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "brainbeat");
            Assert.Equal("Fz", brainbeat.SelectedChannel);
            Assert.Equal("Pz", brainbeat.SecondaryChannel);

            var iapf = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "iapf");
            Assert.Equal("O1", iapf.SelectedChannel);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "iapf" && c.Enabled &&
                c.Config.GetProperty("step_s").GetDouble() == 2d);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "theta_beta" && c.Enabled &&
                c.Config.GetProperty("step_s").GetDouble() == 1d);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "brainbeat" && c.Enabled &&
                c.Config.GetProperty("step_s").GetDouble() == 1d);
            backend.Publish(CreateScalarUpdate(sessionId, "iapf", 1, ["O1"], 10.25, "Hz", "iapf-live-v2",
                evidence: new { extensions = new { iapf_evidence = new { source = "peak", peak_hz = 10.25, cog_hz = 9.62 } } }));
            backend.Publish(CreateScalarUpdate(sessionId, "theta_beta", 1, ["Fz"], 1.85, "dimensionless", "theta-beta-live-v1"));
            backend.Publish(CreateScalarUpdate(sessionId, "brainbeat", 1, ["Fz", "Pz"], 1.42, "ratio", "brainbeat-classic-live-v1"));
            FlushDispatcher(dispatcher);

            var tb = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "theta_beta");
            Assert.Equal("cog", iapf.ScalarAnalysis!.IapfDisplayMode);
            Assert.Equal("COG 9.62 Hz", iapf.ScalarAnalysis.LatestValueText);
            Assert.Contains("综合 IAPF 10.25 Hz", iapf.ScalarAnalysis.DetailText);
            Assert.Contains("实时窗内零相位 1–50 Hz", iapf.ScalarAnalysis.DetailText);
            Assert.Contains("陷波 50 Hz (Q=30)", iapf.ScalarAnalysis.DetailText);
            Assert.Contains("与回溯连续预处理不等价", iapf.ScalarAnalysis.DetailText);
            Assert.Equal(9.62, Assert.Single(iapf.ScalarAnalysis.Points).CogHz);

            var callCountBeforeSwitch = backend.Calls.Count;
            iapf.ScalarAnalysis.IapfDisplayMode = "peak";
            Assert.Equal(callCountBeforeSwitch, backend.Calls.Count);
            Assert.Equal("10.25 Hz", iapf.ScalarAnalysis.LatestValueText);
            Assert.Contains("峰值估计", iapf.ScalarAnalysis.DetailText);
            Assert.Contains("COG 9.62 Hz", iapf.ScalarAnalysis.DetailText);

            backend.Publish(CreateScalarUpdate(sessionId, "iapf", 2, ["O1"], 7.5, "Hz", "iapf-live-v2",
                start: 2, end: 22,
                evidence: new { extensions = new { iapf_evidence = new { source = "cog", peak_hz = 9.48, cog_hz = 7.5, split_peak = true } } }));
            FlushDispatcher(dispatcher);
            Assert.Contains("双峰回退重心", iapf.ScalarAnalysis.DetailText);
            Assert.Equal("7.5 Hz", iapf.ScalarAnalysis.LatestValueText);
            Assert.Equal("1.85", tb.ScalarAnalysis!.LatestValueText);
            Assert.Equal("1.42 ratio", brainbeat.ScalarAnalysis!.LatestValueText);

            tb.ScalarAnalysis.FrequencyMode = "individualized";
            FlushDispatcher(dispatcher);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "theta_beta" && c.Enabled &&
                c.Config.GetProperty("frequency_mode").GetString() == "individualized");

            await workspace.CloseSlotAsync("brainbeat");
            FlushDispatcher(dispatcher);
            Assert.Equal(5, workspace.RightDockedSlots.Count);
            Assert.False(workspace.IsBrainbeatOpen);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "brainbeat" && !c.Enabled);
        });

    [Fact]
    public void Brainbeat_migrates_fz_pz_across_montage_and_handles_missing_pz_gracefully() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeScalarBackend(sessionId, ["Fz", "O1"]);
            Exception? reportedError = null;
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, ex => reportedError = ex);

            await workspace.OpenOrFocusAsync("brainbeat");
            FlushDispatcher(dispatcher);

            Assert.Null(reportedError);
            var card = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "brainbeat");
            Assert.Equal("Fz", card.SelectedChannel);
            Assert.Null(card.SecondaryChannel);
            Assert.Empty(card.SecondaryChannels);
            Assert.Contains("Fz 与 Pz", card.StatusText);
            Assert.DoesNotContain(backend.Calls, c => c.AlgorithmId == "brainbeat" && c.Enabled);

            var bipolarMontage = CreateMontage(2,
                ("Fz-Cz", "Fz", MontageNegativeKind.Channel, ["Cz"]),
                ("Pz-Oz", "Pz", MontageNegativeKind.Channel, ["Oz"]),
                ("Fz-REF", "Fz", MontageNegativeKind.OriginalHardwareReference, []),
                ("Pz-AVG", "Pz", MontageNegativeKind.Mean, ["Fz", "Pz"]));
            backend.RaiseMontageChanged(bipolarMontage);
            FlushDispatcher(dispatcher);
            Assert.Equal(["Fz-REF"], card.Channels);
            Assert.Equal(["Pz-AVG"], card.SecondaryChannels);
            Assert.Equal("Fz-REF", card.SelectedChannel);
            Assert.Equal("Pz-AVG", card.SecondaryChannel);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "brainbeat" && c.Enabled);

            var montage = CreateMontage(3,
                ("Fz-AVG", "Fz", MontageNegativeKind.Mean, ["Fz", "Pz"]),
                ("Pz-AVG", "Pz", MontageNegativeKind.Mean, ["Fz", "Pz"]));
            backend.RaiseMontageChanged(montage);
            FlushDispatcher(dispatcher);

            Assert.Equal("Fz-AVG", card.SelectedChannel);
            Assert.Equal("Pz-AVG", card.SecondaryChannel);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "brainbeat" && c.Enabled &&
                c.Config.GetProperty("channels").EnumerateArray().Select(e => e.GetString()).SequenceEqual(["Fz-AVG", "Pz-AVG"]));
        });

    [Fact]
    public void Brainbeat_channel_options_exclude_bipolar_derivations() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var montage = CreateMontage(2,
                ("Fz-Cz", "Fz", MontageNegativeKind.Channel, ["Cz"]),
                ("Pz-Oz", "Pz", MontageNegativeKind.Channel, ["Oz"]));
            Assert.Empty(LiveScalarPresentation.BrainbeatChannelOptions(["Fz-Cz", "Pz-Oz"], montage, "Fz"));
            Assert.Empty(LiveScalarPresentation.BrainbeatChannelOptions(["Fz-Cz", "Pz-Oz"], montage, "Pz"));
        });

    [Theory]
    [InlineData(MontageNegativeKind.OriginalHardwareReference, 0, true)]
    [InlineData(MontageNegativeKind.Mean, 2, true)]
    [InlineData(MontageNegativeKind.Channel, 1, false)]
    [InlineData(MontageNegativeKind.SpecifiedPair, 2, false)]
    [InlineData(MontageNegativeKind.OriginalHardwareReference, 1, false)]
    [InlineData(MontageNegativeKind.Mean, 1, false)]
    [InlineData(MontageNegativeKind.Mean, 0, false)]
    public void Brainbeat_channel_options_validate_formula_despite_supported_name(
        MontageNegativeKind kind, int negativeCount, bool accepted)
    {
        foreach (var role in new[] { "Fz", "Pz" })
        {
            var name = $"{role}-AVG";
            var negatives = new[] { "Fz", "Pz" }.Take(negativeCount).ToArray();
            var montage = CreateMontage(1, (name, role, kind, negatives));
            var options = LiveScalarPresentation.BrainbeatChannelOptions([name], montage, role);
            Assert.Equal(accepted ? new[] { name } : Array.Empty<string>(), options);
            var wrongPositive = CreateMontage(1, (name, "Cz", kind, negatives));
            Assert.Empty(LiveScalarPresentation.BrainbeatChannelOptions([name], wrongPositive, role));
        }
    }

    [Fact]
    public void Overloaded_update_preserves_existing_scalar_points_and_clears_on_ready() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeScalarBackend(sessionId, ["Fz", "Pz"]);
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, _ => { });

            await workspace.OpenOrFocusAsync("iapf");
            FlushDispatcher(dispatcher);
            var card = Assert.Single(workspace.OpenSlots);
            Assert.Equal("Pz", card.SelectedChannel);

            backend.Publish(CreateScalarUpdate(sessionId, "iapf", 1, ["Pz"], 10.1, "Hz", "iapf-live-v2"));
            FlushDispatcher(dispatcher);
            Assert.Single(card.ScalarAnalysis!.Points);
            Assert.Equal("10.1 Hz", card.ScalarAnalysis.LatestValueText);

            backend.Publish(new LiveAnalysisUpdate(
                sessionId, "iapf", 2, "overloaded", 250,
                StartSeconds: 1, EndSeconds: 21,
                FailureCode: "LIVE_ANALYSIS_OVERLOADED",
                Message: "实时分析繁忙，当前窗口已跳过。",
                SkippedWindows: 3));
            FlushDispatcher(dispatcher);

            Assert.Single(card.ScalarAnalysis.Points);
            Assert.Equal("10.1 Hz", card.ScalarAnalysis.LatestValueText);
            Assert.Contains("已跳过 3 个窗口", card.StatusText);
            Assert.Contains("已跳过 3 个窗口", card.ScalarAnalysis.StatusText);

            backend.Publish(CreateScalarUpdate(sessionId, "iapf", 3, ["Pz"], 10.4, "Hz", "iapf-live-v2", start: 2, end: 22));
            FlushDispatcher(dispatcher);
            Assert.Equal(2, card.ScalarAnalysis.Points.Count);
            Assert.Equal("10.4 Hz", card.ScalarAnalysis.LatestValueText);
            Assert.Equal("", card.StatusText);
        });

    [Fact]
    public void Iapf_yaxis_stays_fixed_at_8_to_13_and_renders_cog_and_alpha_band()
    {
        var empty = LiveScalarTrendChart.ComputeYAxisRange("iapf");
        Assert.False(empty.AutoRange);
        Assert.Equal(8d, empty.Min);
        Assert.Equal(13d, empty.Max);

        Assert.True(LiveScalarTrendChart.ComputeYAxisRange("theta_beta").AutoRange);
        Assert.True(LiveScalarTrendChart.ComputeYAxisRange("brainbeat").AutoRange);

        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var chart = new LiveScalarTrendChart
            {
                AlgorithmId = "iapf",
                Unit = "Hz",
                Points = new[]
                {
                    new LiveScalarPoint(20d, 10.0, "Complete", "clean", 0d, 20d, null, null, "peak", 10.0, 9.6),
                    new LiveScalarPoint(22d, 7.5, "Complete", "clean", 2d, 22d, null, null, "cog", null, 7.5),
                },
            };
            var surface = Assert.IsType<SciChart.Charting.Visuals.SciChartSurface>(((System.Windows.Controls.Grid)chart.Content).Children[0]);
            var yAxis = Assert.Single(surface.YAxes);
            Assert.False(yAxis.DrawMinorTicks);
            Assert.Equal(8d, Convert.ToDouble(yAxis.VisibleRange.Min));
            Assert.Equal(13d, Convert.ToDouble(yAxis.VisibleRange.Max));
            var band = Assert.IsType<SciChart.Charting.Visuals.Annotations.BoxAnnotation>(Assert.Single(surface.Annotations));
            Assert.False(band.IsHidden);
            Assert.Equal(8d, Convert.ToDouble(band.Y1));
            Assert.Equal(13d, Convert.ToDouble(band.Y2));
            Assert.Equal(4, surface.RenderableSeries.Count);

            // Default "cog" mode: solid COG primary line only, peak/cog markers hidden
            Assert.True(surface.RenderableSeries[0].IsVisible);
            Assert.Equal(2, surface.RenderableSeries[0].DataSeries.Count);
            Assert.Equal(7.5, Convert.ToDouble(surface.RenderableSeries[0].DataSeries.YValues[1]));
            Assert.False(surface.RenderableSeries[1].IsVisible);
            Assert.False(surface.RenderableSeries[2].IsVisible);
            Assert.False(surface.RenderableSeries[3].IsVisible);

            // "compare" mode: solid COG primary line + discrete translucent peak dots + COG fallback markers
            chart.IapfDisplayMode = "compare";
            Assert.True(surface.RenderableSeries[0].IsVisible);
            Assert.False(surface.RenderableSeries[1].IsVisible);
            Assert.True(surface.RenderableSeries[2].IsVisible);
            Assert.True(surface.RenderableSeries[3].IsVisible);
            Assert.Equal(1, surface.RenderableSeries[2].DataSeries.Count);
            Assert.Equal(1, surface.RenderableSeries[3].DataSeries.Count);

            // "peak" mode: dashed COG reference + peak line with NaN gap on COG fallback + markers
            chart.IapfDisplayMode = "peak";
            Assert.True(surface.RenderableSeries[0].IsVisible);
            Assert.True(surface.RenderableSeries[1].IsVisible);
            Assert.Equal(2, surface.RenderableSeries[1].DataSeries.Count);
            Assert.True(double.IsNaN(Convert.ToDouble(surface.RenderableSeries[1].DataSeries.YValues[1])));
            Assert.Equal(1, surface.RenderableSeries[2].DataSeries.Count);
            Assert.Equal(1, surface.RenderableSeries[3].DataSeries.Count);
        });
    }

    [Fact]
    public void Iapf_default_channel_prefers_posterior_regions_with_fallback()
    {
        Assert.Equal("Oz", BrainPlatform.Desktop.Modules.Algorithms.Views.Shared.ScalarTrendChartStyle
            .SelectPreferredIapfChannel(["Fp1", "Fz", "Pz", "O1", "Oz"]));
        Assert.Equal("O1-REF", BrainPlatform.Desktop.Modules.Algorithms.Views.Shared.ScalarTrendChartStyle
            .SelectPreferredIapfChannel(["Fp1-REF", "Fz-REF", "Pz-REF", "O1-REF"]));
        Assert.Equal("Fp1", BrainPlatform.Desktop.Modules.Algorithms.Views.Shared.ScalarTrendChartStyle
            .SelectPreferredIapfChannel(["Fp1", "Fp2", "Fz"]));
    }

    [Fact]
    public void LiveScalarAnalysisPanel_renders_inside_dock_chrome_without_duplicate_titles() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var backend = new FakeScalarBackend(Guid.NewGuid(), ["Fz", "Pz"]);
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, Dispatcher.CurrentDispatcher, _ => { });
            var scalar = new LiveScalarAnalysisViewModel("brainbeat", "ratio", 20d);
            var card = new LiveAnalysisCardViewModel(workspace, "brainbeat", "经典 BrainBeat", "BrainBeat",
                LiveAnalysisPanelKind.Scalar, ["Fz", "Pz"], "Fz", null,
                scalarAnalysis: scalar, windowSeconds: 20d, hasSecondaryChannel: true, initialSecondaryChannel: "Pz");

            var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(LiveScalarAnalysisPanel)) };
            var chrome = new DockCardChrome(card.AlgorithmId, card.DisplayName, template: template, slotData: card);
            chrome.SetChrome(showDragBar: true, showCardBorder: true, isFocused: true);
            chrome.Measure(new Size(640, 480));
            chrome.Arrange(new Rect(0, 0, 640, 480));
            chrome.UpdateLayout();

            var shell = FindDescendant<AnalysisPanelShell>(chrome);
            Assert.NotNull(shell);
            Assert.Equal("BrainBeat", shell!.Title);
        });

    private static LiveAnalysisUpdate CreateScalarUpdate(
        Guid sessionId,
        string algorithmId,
        long revision,
        IReadOnlyList<string> channels,
        double value,
        string unit,
        string scientificIdentity,
        double start = 0d,
        double end = 20d,
        object? evidence = null)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            schema_version = "live-scalar-result-v1",
            type = "live_scalar_result",
            acquisition_session_id = sessionId.ToString("N"),
            algorithm_id = algorithmId,
            scientific_identity = scientificIdentity,
            implementation_identity = $"{algorithmId}-live-window-v1",
            state = "ready",
            channels,
            start_sample = (long)(start * 250),
            end_sample = (long)(end * 250),
            start_s = start,
            end_s = end,
            time_s = end,
            value,
            unit,
            quality = "clean",
            failure = (object?)null,
            evidence = evidence ?? new { },
            provenance = new
            {
                preprocessing_scope = "window_local_zero_phase",
                filter_bandpass_hz = new[] { 1d, 50d },
                notch_hz = 50d,
                notch_quality_factor = 30d,
                equivalent_to_offline_continuous_preprocessing = false,
            },
        });
        return new LiveAnalysisUpdate(sessionId, algorithmId, revision, "ready", 250, payload, start, end, scientificIdentity);
    }

    private static LiveMontageSnapshot CreateMontage(long revision,
        params (string Name, string Positive, MontageNegativeKind NegativeKind, string[] Negatives)[] outputs)
    {
        var now = DateTimeOffset.UtcNow;
        var sourceLabels = outputs.SelectMany(item => item.Negatives.Prepend(item.Positive))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = sourceLabels.Select((label, idx) => new ChannelConfigurationEntry(
            idx,
            AcquisitionChannelKind.Reference,
            label,
            true,
            idx,
            null,
            null)).ToArray();
        var channelProfile = new ChannelConfigurationProfile(
            "cfg-1", "CFG", "", ChannelConfigurationSource.User, "dev",
            entries, now, now, "dev-1", "1", "10-20", "1", "1");
        var derived = outputs.Select((item, idx) => new DerivedMontageChannel(
            item.Name,
            item.Positive,
            item.NegativeKind,
            item.Negatives,
            idx)).ToArray();
        var montage = new MontageProfile(
            "m1", "M1", "", MontageProfileSource.User, "dev", "cfg-1",
            channelProfile, derived, 1, now, now, "fp-m1");
        return new LiveMontageSnapshot(montage, revision, 0, outputs.Select(o => o.Name).ToArray(), $"fp-{revision}");
    }

    private static void RunWithDispatcher(Func<Dispatcher, Task> body) =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = body(dispatcher);
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        });

    private static void FlushDispatcher(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private sealed class FakeScalarBackend : ILiveAnalysisBackend
    {
        public FakeScalarBackend(Guid sessionId, IReadOnlyList<string> channelNames)
        {
            State = new AcquisitionStateSnapshot(AcquisitionState.Previewing, "预览中", sessionId, DateTimeOffset.UtcNow);
            StreamMetadata = new AcquisitionStreamMetadata(
                "dev",
                "dev",
                250,
                channelNames.Select((name, idx) => new AcquisitionChannel(idx, idx, name, AcquisitionChannelKind.Reference, "V")).ToArray(),
                1,
                DateTimeOffset.UtcNow);
        }

        public List<(string AlgorithmId, bool Enabled, JsonElement Config)> Calls { get; } = [];
        public AcquisitionStateSnapshot State { get; }
        public AcquisitionStreamMetadata? StreamMetadata { get; }
        public LiveMontageSnapshot? LiveMontage { get; private set; }
        public double? CurrentElapsedSeconds => 25d;
        public event EventHandler<LiveAnalysisUpdate>? LiveAnalysisUpdated;
        public event EventHandler<AcquisitionFault>? LiveAnalysisFaulted;
        public event Action<LiveMontageSnapshot>? LiveMontageChanged;

        public Task SetLiveAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken)
        {
            Calls.Add((algorithmId, enabled, config.ValueKind == JsonValueKind.Undefined ? default : config.Clone()));
            return Task.CompletedTask;
        }

        public void Publish(LiveAnalysisUpdate update) => LiveAnalysisUpdated?.Invoke(this, update);
        public void RaiseMontageChanged(LiveMontageSnapshot montage) { LiveMontage = montage; LiveMontageChanged?.Invoke(montage); }
    }
}
