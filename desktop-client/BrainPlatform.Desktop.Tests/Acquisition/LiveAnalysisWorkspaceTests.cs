using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Acquisition.Views;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;
using BrainPlatform.Desktop.Tests.Review;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveAnalysisWorkspaceTests
{
    [Fact]
    public void Opening_rbp_psd_and_stft_runs_multiple_cards_in_parallel_without_closing_previous() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeLiveBackend(sessionId);
            Exception? reportedError = null;
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, ex => reportedError = ex);

            await workspace.OpenOrFocusAsync("rbp");
            await workspace.OpenOrFocusAsync("psd");
            await workspace.OpenOrFocusAsync("stft");
            FlushDispatcher(dispatcher);

            Assert.Null(reportedError);
            Assert.Equal(3, workspace.RightDockedSlots.Count);
            Assert.True(workspace.IsRbpOpen);
            Assert.True(workspace.IsPsdOpen);
            Assert.True(workspace.IsStftOpen);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "rbp" && c.Enabled);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "rbp-topomap" && c.Enabled);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "psd" && c.Enabled);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "stft" && c.Enabled);
            Assert.DoesNotContain(backend.Calls, c => !c.Enabled);

            backend.Publish(CreatePsdUpdate(sessionId, revision: 1, channel: "Fp1"));
            backend.Publish(CreateRbpUpdate(sessionId, revision: 1));
            FlushDispatcher(dispatcher);

            var rbpCard = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "rbp");
            var psdCard = Assert.Single(workspace.OpenSlots, s => s.AlgorithmId == "psd");
            Assert.NotEmpty(rbpCard.RbpAnalysis!.ChannelHistories);
            Assert.True(psdCard.PsdAnalysis!.HasResult);
            Assert.Equal("", rbpCard.RbpAnalysis.StatusText);
            Assert.Equal("", psdCard.PsdAnalysis.StatusText);

            await workspace.CloseSlotAsync("psd");
            FlushDispatcher(dispatcher);
            Assert.Equal(2, workspace.RightDockedSlots.Count);
            Assert.False(workspace.IsPsdOpen);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "psd" && !c.Enabled);
        });

    [Fact]
    public void Psd_card_switches_between_spectrum_and_five_band_topomap_cleanly() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeLiveBackend(sessionId);
            Exception? reportedError = null;
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, ex => reportedError = ex);

            await workspace.OpenOrFocusAsync("psd");
            var psdCard = Assert.Single(workspace.OpenSlots);
            var psd = psdCard.PsdAnalysis!;

            psd.SelectedView = "脑电地形图";
            FlushDispatcher(dispatcher);

            Assert.Null(reportedError);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "psd" && !c.Enabled);
            Assert.Contains(backend.Calls, c => c.AlgorithmId == "psd-topomap" && c.Enabled);

            backend.Publish(CreatePsdTopomapUpdate(sessionId, revision: 2));
            FlushDispatcher(dispatcher);

            Assert.Equal("", psd.StatusText);
            Assert.Equal(5, psd.TopomapBands.Count);
            Assert.True(psd.IsAllTopomapBandsSelected);
            Assert.Null(psd.SelectedTopomapField);

            psd.SelectedTopomapBandId = "alpha";
            Assert.False(psd.IsAllTopomapBandsSelected);
            Assert.Equal("psd.band-mean-density.alpha", psd.SelectedTopomapField?.FieldId);
        });

    [Fact]
    public void Live_panels_use_Review_aligned_single_row_headers_without_duplicate_titles() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var backend = new FakeLiveBackend(Guid.NewGuid());
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, Dispatcher.CurrentDispatcher, _ => { });
            var rbpCard = new LiveAnalysisCardViewModel(workspace, "rbp", "RBP 相对频段功率", "RBP",
                LiveAnalysisPanelKind.Rbp, ["Fp1"], null, null, rbpAnalysis: new LiveRbpAnalysisViewModel());
            var psdCard = new LiveAnalysisCardViewModel(workspace, "psd", "PSD 功率谱密度", "PSD",
                LiveAnalysisPanelKind.Psd, ["Fp1"], "Fp1", null, psdAnalysis: new LivePsdAnalysisViewModel());
            var stftCard = new LiveAnalysisCardViewModel(workspace, "stft", "STFT 时频图", "STFT",
                LiveAnalysisPanelKind.Stft, ["Fp1"], "Fp1", null, stftAnalysis: new LiveStftAnalysisViewModel());

            foreach (var (card, factory, expectedTitle) in new (LiveAnalysisCardViewModel, Func<FrameworkElement>, string)[]
                     {
                         (rbpCard, () => new LiveRbpAnalysisPanel(), "RBP"),
                         (psdCard, () => new LivePsdAnalysisPanel(), "PSD"),
                         (stftCard, () => new LiveStftAnalysisPanel(), "STFT"),
                     })
            {
                var template = new DataTemplate();
                var factoryRoot = new FrameworkElementFactory(factory().GetType());
                template.VisualTree = factoryRoot;

                var chrome = new DockCardChrome(card.AlgorithmId, card.DisplayName, template: template, slotData: card);
                chrome.SetChrome(showDragBar: true, showCardBorder: true, isFocused: true);
                chrome.Measure(new Size(640, 480));
                chrome.Arrange(new Rect(0, 0, 640, 480));
                chrome.UpdateLayout();

                var shell = FindDescendant<AnalysisPanelShell>(chrome);
                Assert.NotNull(shell);
                Assert.Equal(expectedTitle, shell!.Title);
                Assert.True(shell.IsDockDragEnabled);

                var texts = CollectTextBlocks(shell).Select(t => t.Text).ToArray();
                Assert.DoesNotContain("RBP 图表", texts);
            }
        });

    [Fact]
    public async Task RefreshSession_closes_all_open_cards_when_session_stops()
    {
        var sessionId = Guid.NewGuid();
        var backend = new FakeLiveBackend(sessionId);
        using var workspace = new LiveAnalysisWorkspaceViewModel(backend, Dispatcher.CurrentDispatcher, _ => { });

        await workspace.OpenOrFocusAsync("rbp");
        await workspace.OpenOrFocusAsync("psd");
        workspace.ToggleFloatingSlot("psd");
        Assert.Single(workspace.RightDockedSlots);
        Assert.Single(workspace.FloatingSlots);

        backend.State = new AcquisitionStateSnapshot(AcquisitionState.Stopped, "已停止", null, DateTimeOffset.UtcNow);
        workspace.RefreshSession();

        Assert.False(workspace.IsOpen);
        Assert.Empty(workspace.OpenSlots);
        Assert.Empty(workspace.RightDockedSlots);
        Assert.Empty(workspace.FloatingSlots);
    }

    [Fact]
    public void OnFault_updates_open_cards_with_fault_message_without_removing_slots() =>
        RunWithDispatcher(async dispatcher =>
        {
            var sessionId = Guid.NewGuid();
            var backend = new FakeLiveBackend(sessionId);
            using var workspace = new LiveAnalysisWorkspaceViewModel(backend, dispatcher, _ => { });

            await workspace.OpenOrFocusAsync("psd");
            backend.RaiseFault(new AcquisitionFault("LIVE_FAULT", "实时引擎连接中断", DateTimeOffset.UtcNow));
            FlushDispatcher(dispatcher);

            var psdCard = Assert.Single(workspace.OpenSlots);
            Assert.Equal("实时引擎连接中断", psdCard.StatusText);
            Assert.Equal("实时引擎连接中断", psdCard.PsdAnalysis!.StatusText);
        });

    private static void RunWithDispatcher(Func<Dispatcher, Task> body) =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = body(dispatcher);
            while (!task.IsCompleted)
            {
                FlushDispatcher(dispatcher);
                Thread.Sleep(5);
            }
            task.GetAwaiter().GetResult();
        });

    private static void FlushDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(_ =>
        {
            frame.Continue = false;
            return null;
        }), null);
        Dispatcher.PushFrame(frame);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static IEnumerable<TextBlock> CollectTextBlocks(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var nested in CollectTextBlocks(child)) yield return nested;
        }
    }

    private static LiveAnalysisUpdate CreatePsdUpdate(Guid sessionId, long revision, string channel) =>
        new(sessionId, "psd", revision, "ready", 512,
            Payload: JsonSerializer.SerializeToElement(new
            {
                schema_version = "live-spectrum-result-v1",
                unit = "V^2/Hz",
                channels = new[] { channel },
                frequencies_hz = new[] { 1.0, 2.0, 10.0 },
                values = new[] { new[] { 1e-10, 2e-10, 3e-10 } },
            }),
            StartSeconds: 0, EndSeconds: 10, ScientificIdentity: "psd-live-welch-v1");

    private static LiveAnalysisUpdate CreateRbpUpdate(Guid sessionId, long revision) =>
        new(sessionId, "rbp", revision, "ready", 512,
            Payload: JsonSerializer.SerializeToElement(new
            {
                type = "live_rbp_result",
                schema_version = "live-rbp-result-v1",
                algorithm_id = "rbp",
                scientific_identity = "rbp-live-v1",
                acquisition_session_id = sessionId.ToString("N"),
                state = "ready",
                channels = new[] { "Fp1" },
                start_sample = 0L,
                end_sample = 5120L,
                start_s = 0.0,
                end_s = 10.0,
                values = new Dictionary<string, double?[]>
                {
                    ["delta"] = [0.2], ["theta"] = [0.2], ["alpha"] = [0.3], ["beta"] = [0.2], ["gamma"] = [0.1],
                },
                units = new Dictionary<string, string>
                {
                    ["delta"] = "ratio", ["theta"] = "ratio", ["alpha"] = "ratio", ["beta"] = "ratio", ["gamma"] = "ratio",
                },
                quality = new Dictionary<string, object>
                {
                    ["Fp1"] = new { status = "clean" },
                },
            }),
            StartSeconds: 0, EndSeconds: 10, ScientificIdentity: "rbp-live-v1");

    private static LiveAnalysisUpdate CreatePsdTopomapUpdate(Guid sessionId, long revision)
    {
        var fields = LivePsdAnalysisViewModel.TopomapFieldIds.Select(id => new
        {
            field_id = id,
            label = id,
            unit = "V^2/Hz",
            status = "ready",
            failure = (object?)null,
            points = Array.Empty<object>(),
            included_channels = new[] { "Fp1" },
            excluded_channels = Array.Empty<string>(),
            grid = new
            {
                method = "rbf",
                width = 2,
                height = 2,
                x_min = -1.0,
                x_max = 1.0,
                y_min = -1.0,
                y_max = 1.0,
                head_radius = 1.0,
                values = new[] { new double?[] { 0.1, 0.2 }, [0.3, 0.4] },
            },
        });
        return new LiveAnalysisUpdate(sessionId, "psd-topomap", revision, "ready", 512,
            Payload: JsonSerializer.SerializeToElement(new { schema_version = "live-topomap-result-v1", fields }),
            StartSeconds: 0, EndSeconds: 10, ScientificIdentity: "psd-live-welch-v1");
    }

    private sealed class FakeLiveBackend(Guid sessionId) : ILiveAnalysisBackend
    {
        public AcquisitionStateSnapshot State { get; set; } = new(AcquisitionState.Previewing, "预览中", sessionId, DateTimeOffset.UtcNow);
        public AcquisitionStreamMetadata? StreamMetadata { get; } = new("fake", "fake", 512,
            [new AcquisitionChannel(0, 0, "Fp1", AcquisitionChannelKind.Reference, "V")], 1, DateTimeOffset.UtcNow);
        public LiveMontageSnapshot? LiveMontage => null;
        public double? CurrentElapsedSeconds => 5.0;
        public List<(string AlgorithmId, bool Enabled, JsonElement Config)> Calls { get; } = [];

        public event EventHandler<LiveAnalysisUpdate>? LiveAnalysisUpdated;
        public event EventHandler<AcquisitionFault>? LiveAnalysisFaulted;
        public event Action<LiveMontageSnapshot>? LiveMontageChanged;

        public Task SetLiveAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken)
        {
            Calls.Add((algorithmId, enabled, config.ValueKind == JsonValueKind.Undefined ? default : config.Clone()));
            return Task.CompletedTask;
        }

        public void Publish(LiveAnalysisUpdate update) => LiveAnalysisUpdated?.Invoke(this, update);
        public void RaiseFault(AcquisitionFault fault) => LiveAnalysisFaulted?.Invoke(this, fault);
        public void TriggerMontageChanged(LiveMontageSnapshot montage) => LiveMontageChanged?.Invoke(montage);
    }
}
