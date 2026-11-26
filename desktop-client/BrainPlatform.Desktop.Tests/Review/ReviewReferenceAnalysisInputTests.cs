using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData("psd", false)]
    [InlineData("psd", true)]
    [InlineData("rbp", false)]
    [InlineData("rbp", true)]
    [InlineData("spectral-values", false)]
    [InlineData("spectral-values", true)]
    public async Task ReferencePartition_DrivesAllAnalysisInputsFromBackendLayout(string panelId, bool withMontage)
    {
        var configuration = Configuration();
        var profile = Profile("saved", configuration);
        var suffix = withMontage ? "-AVG" : "";
        var montage = new HistoricalMontageDescriptor(profile.Id, "r1", "fingerprint", "Saved", true, true, null,
            [new("F3-AVG", "F3-AVG", "F3", "V", true, null), new("F7-AVG", "F7-AVG", "F7", "V", true, null)]);
        var recording = new RegisteredRecording("references", "record.edf", ".edf", "now", 500, 100,
            ["F3", "F7"], "sha", withMontage ? [montage] : []);
        var catalogue = new RecordingMontageCatalogResult(withMontage ? profile : null,
            withMontage ? AcquisitionMontageStatus.Available : AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F7"]), withMontage ? [new CompatibleRecordingMontage(profile)] : [], []);
        var handler = new AnalysisLayoutHandler("F7" + suffix);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var psd = new PanelProvider("psd") { SupportsArtifactRemoval = true };
        var rbp = new PanelProvider("rbp") { SupportsArtifactRemoval = true };
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalogue,
            registeredAnalysisRecording: recording, algorithmClient: new HttpAlgorithmClient(http),
            analysisCoordinators: [new ReviewAnalysisCoordinator(psd), new ReviewAnalysisCoordinator(rbp)]);
        await vm.InitializeAsync();
        await vm.Preprocessing.LoadReferenceChannelsAsync();
        vm.Preprocessing.ReferenceChannels.Single(c => c.Name == "F3").IsEog = true;
        vm.Preprocessing.RemovalMethod = "ssp_eog";
        await vm.SeekAsync(10);
        await vm.OpenAnalysisAsync(panelId);

        var configurations = psd.Configurations.Concat(rbp.Configurations).ToArray();
        Assert.NotEmpty(configurations);
        foreach (var config in configurations)
        {
            Assert.Equal(new[] { "F7" + suffix }, config.GetProperty("channels").EnumerateArray().Select(x => x.GetString()));
            if (withMontage) Assert.Equal("F7-AVG", Assert.Single(config.GetProperty("analysis_montage_outputs").EnumerateArray()).GetProperty("output").GetString());
        }
        Assert.Equal(1, handler.LayoutRequests);
        if (panelId != "rbp") Assert.Equal(new[] { "F7" + suffix }, vm.PsdParameters.Channels);
        if (panelId == "spectral-values") Assert.Equal("F7" + suffix, Assert.Single(vm.SpectralValues.Rows).Channel);
    }

    private sealed class AnalysisLayoutHandler(string channel) : HttpMessageHandler
    {
        public int LayoutRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object response;
            if (request.RequestUri!.AbsolutePath.EndsWith("reference-channels", StringComparison.Ordinal))
                response = new ArtifactReferenceCatalogue("references", 500, 50000, [new("F3", "V"), new("F7", "V")]);
            else
            {
                LayoutRequests++;
                response = new PreprocessingAnalysisChannels("references", [channel]);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        }
    }
}
