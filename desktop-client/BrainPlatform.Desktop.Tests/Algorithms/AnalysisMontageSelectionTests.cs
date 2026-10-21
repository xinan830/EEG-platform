using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AnalysisMontageSelectionTests
{
    [Fact]
    public void Analysis_target_binds_montage_selector_and_preserves_role_when_switching_profiles() =>
        WpfTestApplication.Run(() =>
        {
            var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
            var reference = Profile("reference", "REF", true, "F3-REF");
            var average = Profile("average", "AV", false, "F3-AV");
            catalog.MontageProfiles.Add(reference);
            catalog.MontageProfiles.Add(average);
            catalog.SelectedMontageProfile = reference;
            catalog.SelectedChannel = "F3-REF";

            var card = new AnalysisTargetCard { DataContext = catalog };
            card.Measure(new Size(900, 600));
            card.Arrange(new Rect(0, 0, 900, card.DesiredSize.Height));
            card.UpdateLayout();
            var selector = VisualChildren<ComboBox>(card).Single(combo =>
                ReferenceEquals(combo.ItemsSource, catalog.MontageProfiles));

            Assert.Equal(reference, selector.SelectedItem);
            Assert.NotNull(selector.ItemTemplate);
            var selectionPresenter = VisualChildren<ContentPresenter>(selector)
                .Single(presenter => ReferenceEquals(presenter.Content, reference));
            Assert.Same(selector.ItemTemplate, selectionPresenter.ContentTemplate);
            Assert.Equal("F3-REF", catalog.SelectedChannel);
            selector.SelectedItem = average;
            card.UpdateLayout();

            Assert.Equal(average, catalog.SelectedMontageProfile);
            Assert.Equal("F3-AV", catalog.SelectedChannel);
            Assert.Equal("AV", catalog.AnalysisMontageText);
            Assert.Contains("F3-AV", catalog.AnalysisMontageDetailsText);
        });

    [Fact]
    public void Run_payload_carries_selected_montage_identity_and_role_mapping()
    {
        var algorithm = new AlgorithmCatalogItem("official", "peak_frequency", "fixture-v1", "峰频率", "Peak", "", [],
            ["static"], default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var montage = Profile("average", "AV", false, "F3-AV");

        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, "F3-AV", new TimeRange(0, 10), "static",
            montage: montage);

        Assert.Equal("average", config.GetProperty("analysis_montage_id").GetString());
        Assert.Equal("revision-1", config.GetProperty("analysis_montage_revision").GetString());
        Assert.Equal("fingerprint-average", config.GetProperty("analysis_montage_fingerprint").GetString());
        var output = Assert.Single(config.GetProperty("analysis_montage_outputs").EnumerateArray());
        Assert.Equal("channel1", output.GetProperty("role").GetString());
        Assert.Equal("F3-AV", output.GetProperty("output").GetString());
        Assert.Equal("V", output.GetProperty("unit").GetString());
    }

    [Fact]
    public void Single_channel_run_payload_includes_only_the_selected_montage_output()
    {
        var algorithm = new AlgorithmCatalogItem("official", "stft", "fixture-v1", "时频分析", "STFT", "", [],
            ["static"], default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var montage = MultiOutputProfile();

        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, "Fp1-REF", new TimeRange(0, 10), "static",
            montage: montage);

        var output = Assert.Single(config.GetProperty("analysis_montage_outputs").EnumerateArray());
        Assert.Equal("Fp1", output.GetProperty("role").GetString());
        Assert.Equal("Fp1-REF", output.GetProperty("output").GetString());
    }

    [Fact]
    public void Paired_run_payload_includes_only_both_selected_montage_outputs()
    {
        var algorithm = new AlgorithmCatalogItem("official", "faa", "fixture-v1", "额叶 Alpha 不对称性", "FAA", "", [],
            ["static"], default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var montage = MultiOutputProfile();

        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, "Fp1-REF", new TimeRange(0, 10), "static",
            f4Channel: "Fp2-REF", montage: montage);

        var outputs = config.GetProperty("analysis_montage_outputs").EnumerateArray().ToArray();
        Assert.Equal(["Fp1-REF", "Fp2-REF"], outputs.Select(output => output.GetProperty("output").GetString()));
    }

    private static HistoricalMontageDescriptor Profile(string id, string label, bool isDefault, string output) => new(
        id, "revision-1", $"fingerprint-{id}", label, isDefault, true, null,
        [new HistoricalMontageOutput(output, output, "channel1", "V", true, null)]);

    private static HistoricalMontageDescriptor MultiOutputProfile() => new(
        "reference", "revision-1", "fingerprint-reference", "301导联配置", true, true, null,
        [
            new HistoricalMontageOutput("Fp1-REF", "Fp1-REF", "Fp1", "V", true, null),
            new HistoricalMontageOutput("Fp2-REF", "Fp2-REF", "Fp2", "V", true, null),
            new HistoricalMontageOutput("F7-REF", "F7-REF", "F7", "V", true, null),
            new HistoricalMontageOutput("F8-REF", "F8-REF", "F8", "V", true, null),
        ]);

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private sealed class UnsupportedAlgorithmClient : BrainPlatform.Desktop.Modules.Algorithms.Api.IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
