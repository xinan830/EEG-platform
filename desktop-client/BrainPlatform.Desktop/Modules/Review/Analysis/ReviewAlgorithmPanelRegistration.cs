using System.Text.Json;
using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Algorithm-owned configuration and presentation composed by the shared Review lifecycle.</summary>
public sealed record ReviewAlgorithmPanelRegistration(
    string AlgorithmId,
    string DisplayName,
    ReviewAnalysisPanelKind PanelKind,
    INotifyPropertyChanged Configuration,
    object Presentation,
    Action<RegisteredRecording, AlgorithmCatalogItem, HistoricalMontageDescriptor?> Initialize,
    Func<double, double, JsonElement> BuildConfiguration,
    Action<WindowPreviewResponse?, double> Apply,
    Func<IAlgorithmClient, IReviewAlgorithmAdapter> CreateAdapter)
{
    public ReviewAnalysisCoordinator CreateCoordinator(IAlgorithmClient client)
    {
        var adapter = CreateAdapter(client);
        if (!string.Equals(adapter.AlgorithmId, AlgorithmId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Review 面板 {AlgorithmId} 收到不匹配的执行 adapter {adapter.AlgorithmId}。");
        return new ReviewAnalysisCoordinator(adapter);
    }
}
