using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class ReviewPreprocessingTests
{
    [Fact]
    public async Task DedicatedReferences_ReachIcaPreviewAndAlgorithm_AndChangeInvalidatesConfirmation()
    {
        var client = new ReferenceClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.ReferenceChannels.Single(c => c.Name == "EOG2").IsEog = true;
        vm.SelectedEcgChannel = "ECG1";
        var report = await vm.InspectIcaForDialogAsync();
        Assert.Equal(new[] { "EOG1", "EOG2" }, client.Eog);
        Assert.Equal(new[] { "ECG1" }, client.Ecg);
        Assert.False(client.AllowProxy);
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        var payload = vm.BuildPayload(true).ArtifactRemoval;
        Assert.Equal(client.Eog, payload.EogChannels);
        Assert.Equal(client.Ecg, payload.EcgChannels);
        var config = vm.BuildAlgorithmConfigDictionary(true)!;
        var removal = Assert.IsType<Dictionary<string, object?>>(config["artifact_removal"]);
        Assert.Equal(new[] { "ECG1" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(removal["ecg_channels"]));
        vm.SelectedEcgChannel = "";
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Null(vm.ConfirmedIcaExcluded);
        Assert.Contains("辅助通道已变更", vm.IcaSummaryText);
    }

    [Fact]
    public async Task ReloadingUnchangedReferences_PreservesConfirmation_AndMissingReferenceInvalidates()
    {
        var client = new ReferenceClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.SelectedEcgChannel = "ECG1";
        var report = await vm.InspectIcaForDialogAsync();
        vm.ConfirmIcaSelection([0], report.IcaFitFingerprint);
        await vm.LoadReferenceChannelsAsync();
        Assert.Equal(report.IcaFitFingerprint, vm.IcaFitFingerprint);
        Assert.Equal(new[] { "EOG1" }, vm.SelectedEogChannels);
        Assert.Equal("ECG1", vm.SelectedEcgChannel);
        client.MissingReference = true;
        await vm.LoadReferenceChannelsAsync();
        Assert.Null(vm.IcaFitFingerprint);
        Assert.Empty(vm.SelectedEogChannels);
    }

    [Fact]
    public async Task Reset_DiscardsLateReferenceCatalogue()
    {
        var client = new ReferenceClient { DelayDiscovery = true };
        using var vm = CreateViewModel(client, out _);
        var loading = vm.LoadReferenceChannelsAsync();
        await client.Started.Task;
        vm.ResetAll();
        client.Release.SetResult();
        await loading;
        Assert.Empty(vm.ReferenceChannels);
        Assert.Empty(vm.SelectedEcgChannels);
    }

    [Fact]
    public async Task ReferenceSelectionDuringIca_DiscardsOldResponse()
    {
        var client = new ControlledClient { DelayIca = true };
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        var request = vm.InspectIcaForDialogAsync();
        await client.IcaStarted.Task;
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        client.IcaRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Empty(vm.IcaComponents);
        Assert.Null(vm.IcaFitFingerprint);
    }

    [Fact]
    public async Task OverlappingRoles_AreVisibleAndRejectedBeforeIcaRequest()
    {
        var client = new ReferenceClient();
        using var vm = CreateViewModel(client, out _);
        await vm.LoadReferenceChannelsAsync();
        vm.ReferenceChannels.Single(c => c.Name == "EOG1").IsEog = true;
        vm.SelectedEcgChannel = "EOG1";
        Assert.Contains("不能同时", vm.ReferenceStatusText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.InspectIcaForDialogAsync());
        Assert.Null(client.Eog);
    }

    [Fact]
    public void ComponentCard_ShowsBothReferenceScoresIncludingLowEcgScore()
    {
        var card = new BrainPlatform.Desktop.Modules.Review.Preprocessing.IcaComponentCardViewModel(
            new IcaComponentItem(0, 0.8, 0.2, 0.4, true, true, null, null, 0), true);
        Assert.Contains("EOG", card.ScoreBadgeText);
        Assert.Contains("ECG 0.20", card.ScoreBadgeText);
    }

    [Fact]
    public void ReferenceDialog_RendersSelectorsAndReloadKeepsBoundSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var vm = CreateViewModel(new ReferenceClient(), out _);
                vm.LoadReferenceChannelsAsync().GetAwaiter().GetResult();
                vm.RemovalMethod = "ica_infomax";
                var dialog = new BrainPlatform.Desktop.Modules.Review.Views.Preprocessing.ArtifactRemovalStepDialog(vm);
                try
                {
                    var surface = (System.Windows.FrameworkElement)dialog.Content;
                    surface.Measure(new System.Windows.Size(740, 560));
                    surface.Arrange(new System.Windows.Rect(0, 0, 740, 560));
                    surface.UpdateLayout();
                    var ecg = (System.Windows.Controls.ComboBox)dialog.FindName("EcgReferenceChoice");
                    var eog = (System.Windows.Controls.ItemsControl)dialog.FindName("EogReferenceChoices");
                    Assert.Equal(4, eog.Items.Count);
                    Assert.True(ecg.IsEnabled);
                    ecg.SelectedItem = "ECG1";
                    Assert.Equal("ECG1", vm.SelectedEcgChannel);
                    vm.LoadReferenceChannelsAsync().GetAwaiter().GetResult();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert.Equal("ECG1", vm.SelectedEcgChannel);
                    Assert.Equal("ECG1", ecg.SelectedItem);
                    surface.UpdateLayout();
                    Assert.Equal("ECG1", ecg.SelectionBoxItem);
                    var path = Environment.GetEnvironmentVariable("ARTIFACT_REFERENCE_PREVIEW_PATH");
                    if (path is not null)
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(740, 560, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var stream = File.Create(path);
                        encoder.Save(stream);
                    }
                }
                finally { dialog.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class ReferenceClient : FakePreprocessingClient
    {
        public bool DelayDiscovery { get; init; }
        public bool MissingReference { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string>? Eog { get; private set; }
        public IReadOnlyList<string>? Ecg { get; private set; }
        public bool AllowProxy { get; private set; }

        public override async Task<ArtifactReferenceCatalogue> GetArtifactReferenceChannelsAsync(
            string recordingId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (DelayDiscovery) await Release.Task;
            var catalogue = await base.GetArtifactReferenceChannelsAsync(recordingId, cancellationToken);
            return MissingReference ? catalogue with { Channels = catalogue.Channels.Where(c => c.Name != "EOG1").ToArray() } : catalogue;
        }

        public override Task<IcaInspectionResponse> InspectIcaComponentsAsync(
            string recordingId, string? montageId, double lowHz, double highHz, double? notchHz,
            IReadOnlyList<string>? badChannels, string badChannelPolicy, string artifactPolicy,
            double? peakToPeakUv, double? muscleZScoreThreshold, bool allowFrontalEogProxy,
            int? icaComponentCount, double icaEogThreshold, IReadOnlyList<string>? eogChannels,
            IReadOnlyList<string>? ecgChannels, CancellationToken cancellationToken)
        {
            Eog = eogChannels;
            Ecg = ecgChannels;
            AllowProxy = allowFrontalEogProxy;
            return base.InspectIcaComponentsAsync(recordingId, montageId, lowHz, highHz, notchHz,
                badChannels, badChannelPolicy, artifactPolicy, peakToPeakUv, muscleZScoreThreshold,
                allowFrontalEogProxy, icaComponentCount, icaEogThreshold, eogChannels, ecgChannels, cancellationToken);
        }
    }
}
