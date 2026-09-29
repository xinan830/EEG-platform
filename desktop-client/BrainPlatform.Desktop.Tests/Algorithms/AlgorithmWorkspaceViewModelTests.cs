using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmWorkspaceViewModelTests
{
    [Theory]
    [InlineData("psd", AlgorithmDetailKind.FrequencySpectrum)]
    [InlineData("stft", AlgorithmDetailKind.TimeFrequency)]
    [InlineData("rbp", AlgorithmDetailKind.RelativeBandPower)]
    [InlineData("faa", AlgorithmDetailKind.Faa)]
    [InlineData("iapf", AlgorithmDetailKind.Iapf)]
    [InlineData("peak_frequency", AlgorithmDetailKind.PeakFrequency)]
    [InlineData("band_ratio", AlgorithmDetailKind.BandRatio)]
    [InlineData("theta_beta", AlgorithmDetailKind.ThetaBeta)]
    [InlineData("brainbeat", AlgorithmDetailKind.Brainbeat)]
    public void Detail_UsesAlgorithmSpecificPresentationKind(string id, AlgorithmDetailKind expected)
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        var workspace = new AlgorithmWorkspaceViewModel(catalog);
        catalog.SelectedAlgorithm = Algorithm(id);

        Assert.Equal(expected, workspace.Detail.DetailKind);
        Assert.True(workspace.Detail.HasAlgorithm);
    }

    [Fact]
    public void Workspace_OpensSelectedDetailAndReturnsToList()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        var workspace = new AlgorithmWorkspaceViewModel(catalog);
        catalog.SelectedAlgorithm = Algorithm("psd");

        Assert.True(workspace.OpenDetailCommand.CanExecute(null));
        workspace.OpenDetailCommand.Execute(null);
        Assert.True(workspace.IsDetailOpen);
        Assert.False(workspace.IsListOpen);

        workspace.BackToListCommand.Execute(null);
        Assert.False(workspace.IsDetailOpen);
        Assert.True(workspace.IsListOpen);
    }

    [Fact]
    public void AnalysisModeSwitch_UpdatesDynamicStateAndParametersImmediately()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        var context = new AnalysisContextViewModel(catalog);
        var notifications = new List<string>();
        context.PropertyChanged += (_, args) => notifications.Add(args.PropertyName ?? "");
        catalog.SelectedAlgorithm = Algorithm("psd") with
        {
            Modes = ["static", "dynamic"],
            DynamicPolicy = new DynamicAnalysisPolicy(4, [5, 10, 20], 10, 1, true),
        };
        Assert.False(context.IsDynamicMode);
        Assert.Contains(nameof(AnalysisContextViewModel.DynamicWindowOptions), notifications);
        notifications.Clear();
        context.SelectDynamicModeCommand.Execute(null);

        Assert.True(context.IsDynamicMode);
        Assert.Contains(nameof(AnalysisContextViewModel.IsDynamicMode), notifications);
        Assert.Contains(nameof(AnalysisContextViewModel.SelectedAnalysisMode), notifications);
        Assert.Equal(new[] { 5d, 10d, 20d }, context.DynamicWindowOptions);
        Assert.Equal(10d, context.SelectedDynamicWindowSeconds);
        Assert.Equal("1", context.DynamicStepText);

        context.SelectStaticModeCommand.Execute(null);
        Assert.False(context.IsDynamicMode);
    }

    [Fact]
    public void CatalogFiltersUseBackendMetadataWithoutChangingItems()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        var psd = Algorithm("psd") with
        {
            DisplayNameZh = "功率谱密度",
            Description = "计算信号功率谱密度",
            Modes = ["static", "dynamic"]
        };
        var faa = Algorithm("faa") with { DisplayNameZh = "额叶 Alpha 不对称性" };
        catalog.Items.Add(psd);
        catalog.Items.Add(faa);

        catalog.SelectedTypeFilter = "频谱";

        Assert.Single(catalog.VisibleItems);
        Assert.Equal("psd", catalog.VisibleItems[0].Id);
        Assert.Equal(2, catalog.Items.Count);
        Assert.Equal("频谱图", psd.ResultTypeDisplay);
        Assert.Equal("可用", psd.StatusDisplay);
    }

    [Fact]
    public void CatalogSearchMatchesNameCodeAndDescription()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        catalog.Items.Add(Algorithm("psd") with { DisplayNameZh = "功率谱密度", Description = "频域功率分布" });
        catalog.Items.Add(Algorithm("faa") with { DisplayNameZh = "额叶 Alpha 不对称性" });

        catalog.SearchText = "频域";

        Assert.Single(catalog.VisibleItems);
        Assert.Equal("psd", catalog.VisibleItems[0].Id);
    }

    private static AlgorithmCatalogItem Algorithm(string id) => new(
        "official", id, "v1", id, id.ToUpperInvariant(), "", [], ["static"], default,
        new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

    private sealed class UnsupportedAlgorithmClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
