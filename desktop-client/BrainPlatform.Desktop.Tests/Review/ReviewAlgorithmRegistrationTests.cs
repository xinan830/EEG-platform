using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using System.Xml.Linq;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Registered_panels_create_their_own_adapters_and_preserve_specialized_capabilities()
    {
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100), catalog);
        using var http = new HttpClient();
        var client = new HttpAlgorithmClient(http);
        var panels = ReviewAlgorithmPanels.Create(vm);

        Assert.Equal(new[] { "brainbeat", "brainbeat_individualized", "faa", "iapf", "psd", "rbp", "stft", "theta_beta", "tnk_biofeedback" },
            panels.Keys.Order(StringComparer.Ordinal));
        foreach (var panel in panels.Values)
        {
            var coordinator = panel.CreateCoordinator(client);
            Assert.Equal(panel.AlgorithmId, coordinator.AlgorithmId);
            Assert.Equal(panel.AlgorithmId is "psd" or "rbp",
                coordinator.GetCapability<IReviewTopomapProvider>() is not null);
            Assert.Equal(panel.AlgorithmId == "tnk_biofeedback",
                coordinator.GetCapability<IReviewTnkPlaybackEvents>() is not null);
        }
        Assert.Same(vm.PsdParameters, panels["psd"].Configuration);
        Assert.Same(vm.PsdAnalysis, panels["psd"].Presentation);
        Assert.Same(vm.RbpAnalysis, panels["rbp"].Presentation);
        Assert.Same(vm.IapfAnalysis, panels["iapf"].Presentation);
        Assert.Same(vm.ThetaBetaAnalysis, panels["theta_beta"].Presentation);
        Assert.Same(vm.BrainbeatParameters, panels["brainbeat"].Configuration);
        Assert.Same(vm.IndividualizedBrainbeatParameters, panels["brainbeat_individualized"].Configuration);
        var wrong = panels["psd"] with { CreateAdapter = c => new RbpReviewAnalysisAdapter(c) };
        var error = Assert.Throws<InvalidOperationException>(() => wrong.CreateCoordinator(client));
        Assert.Contains("psd", error.Message);
        Assert.Contains("rbp", error.Message);
    }

    [Theory]
    [InlineData("faa")]
    [InlineData("iapf")]
    [InlineData("theta_beta")]
    [InlineData("brainbeat")]
    [InlineData("brainbeat_individualized")]
    public async Task Active_scalar_binding_comes_from_selected_registration(string algorithmId)
    {
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(Configuration()), 100), catalog,
            recordingDirectory: "_temp/scalar-review-fixture",
            analysisCoordinators: [new ReviewAnalysisCoordinator(new PanelProvider(algorithmId))]);
        await vm.InitializeAsync();
        await vm.SeekAsync(25);
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        var command = algorithmId switch
        {
            "faa" => vm.OpenFaaAnalysisCommand,
            "iapf" => vm.OpenIapfAnalysisCommand,
            "theta_beta" => vm.OpenThetaBetaAnalysisCommand,
            "brainbeat" => vm.OpenBrainbeatAnalysisCommand,
            "brainbeat_individualized" => vm.OpenIndividualizedBrainbeatAnalysisCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmId)),
        };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(null)) completed.TrySetResult();
        };
        command.Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsAnalysisAttached);

        var panel = Assert.IsType<ReviewAlgorithmPanelRegistration>(vm.ActiveAnalysisPanel);
        Assert.Equal(algorithmId, panel.AlgorithmId);
        Assert.Same(panel.Configuration, vm.ActiveScalarParameters);
        Assert.Same(panel.Presentation, vm.ActiveScalarAnalysis);
        Assert.Equal(ReviewAnalysisPanelKind.Scalar, panel.PanelKind);
        Assert.Equal(panel.DisplayName, vm.AnalysisPanelTitle);
        Assert.Contains(nameof(vm.ActiveAnalysisPanel), notified);
        await vm.CloseAnalysisAsync();
        Assert.Null(vm.ActiveAnalysisPanel);
        Assert.Null(vm.ActiveScalarAnalysis);
    }

    [Fact]
    public void Review_xaml_covers_registered_panel_kinds_and_both_algorithm_entry_surfaces()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordingReviewView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var triggers = document.Descendants(wpf + "DataTrigger")
            .Where(item => (string?)item.Attribute("Binding") == "{Binding ActiveAnalysisPanelKind}").ToArray();
        Assert.Equal(Enum.GetNames<ReviewAnalysisPanelKind>().Order(), triggers.Select(item =>
            ((string)item.Attribute("Value")!).Split('.').Last()[..^1]).Order());
        foreach (var trigger in triggers)
        {
            var kind = ((string)trigger.Attribute("Value")!).Split('.').Last()[..^1];
            var setter = Assert.Single(trigger.Elements(wpf + "Setter"));
            Assert.Equal("ContentTemplate", (string?)setter.Attribute("Property"));
            Assert.Equal($"{{StaticResource {kind}PanelTemplate}}", (string?)setter.Attribute("Value"));
        }
        var commandNames = new[]
        {
            "OpenPsdAnalysisCommand", "OpenRbpAnalysisCommand", "OpenStftAnalysisCommand", "OpenTnkAnalysisCommand",
            "OpenFaaAnalysisCommand", "OpenIapfAnalysisCommand", "OpenThetaBetaAnalysisCommand",
            "OpenBrainbeatAnalysisCommand", "OpenIndividualizedBrainbeatAnalysisCommand",
        };
        foreach (var command in commandNames)
        {
            Assert.NotNull(typeof(RecordingReviewViewModel).GetProperty(command));
            var entries = document.Descendants().Where(item =>
                (string?)item.Attribute("Command") == $"{{Binding {command}}}").ToArray();
            Assert.Equal(2, entries.Length);
        }
    }
}
