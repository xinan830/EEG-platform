using System.Text.Json;
using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Algorithm-owned configuration and presentation composed by the shared Review lifecycle.</summary>
public sealed record ReviewAlgorithmPanelRegistration(
    WorkspaceMontageSelection<MontageProfile> MontageSelection,
    string AlgorithmId,
    string DisplayName,
    ReviewAnalysisPanelKind PanelKind,
    INotifyPropertyChanged Configuration,
    object Presentation,
    Action<RegisteredRecording, AlgorithmCatalogItem, HistoricalMontageDescriptor?> Initialize,
    Func<double, double, HistoricalMontageDescriptor?, JsonElement> BuildConfiguration,
    Action<RegisteredRecording, HistoricalMontageDescriptor?, HistoricalMontageDescriptor?> RefreshInputOptions,
    Action<WindowPreviewResponse?, double> Apply,
    Func<IAlgorithmClient, IReviewAlgorithmAdapter> CreateAdapter)
{
    public void InitializeForSelection(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selection, HistoricalMontageDescriptor? montage)
    {
        EnsureCurrent(selection);
        Initialize(recording, algorithm, montage);
    }

    public JsonElement BuildConfigurationForSelection(double durationSeconds, double samplingRateHz,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selection, HistoricalMontageDescriptor? montage)
    {
        EnsureCurrent(selection);
        return BuildConfiguration(durationSeconds, samplingRateHz, montage);
    }

    public void RefreshForSelection(RegisteredRecording recording,
        WorkspaceMontageSelectionSnapshot<MontageProfile> previousSelection,
        WorkspaceMontageSelectionSnapshot<MontageProfile> selection,
        HistoricalMontageDescriptor? previousMontage,
        HistoricalMontageDescriptor? montage)
    {
        EnsureCurrent(selection);
        if (selection.RecordingId != MontageSelection.RecordingId ||
            previousSelection.RecordingId != MontageSelection.RecordingId)
            throw new InvalidOperationException("算法面板收到其他工作区的导联快照。");
        RefreshInputOptions(recording, previousMontage, montage);
    }

    public ReviewAnalysisCoordinator CreateCoordinator(IAlgorithmClient client)
    {
        var adapter = CreateAdapter(client);
        if (!string.Equals(adapter.AlgorithmId, AlgorithmId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Review 面板 {AlgorithmId} 收到不匹配的执行 adapter {adapter.AlgorithmId}。");
        return new ReviewAnalysisCoordinator(adapter);
    }

    private void EnsureCurrent(WorkspaceMontageSelectionSnapshot<MontageProfile> selection)
    {
        if (selection.RecordingId != MontageSelection.RecordingId ||
            !ReferenceEquals(MontageSelection.Capture(), selection))
            throw new InvalidOperationException("算法面板拒绝使用过期或属于其他工作区的导联快照。");
    }
}
