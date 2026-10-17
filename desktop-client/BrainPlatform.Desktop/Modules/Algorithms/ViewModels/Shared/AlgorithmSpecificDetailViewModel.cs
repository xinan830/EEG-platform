using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public abstract class AlgorithmSpecificDetailViewModel : ObservableObject
{
    protected AlgorithmSpecificDetailViewModel(AlgorithmListViewModel catalog, string algorithmId)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        AlgorithmId = algorithmId;
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
    }

    public AlgorithmListViewModel Catalog { get; }
    public string AlgorithmId { get; }
    public AlgorithmCatalogItem? Algorithm => Catalog.SelectedAlgorithm?.Id == AlgorithmId ? Catalog.SelectedAlgorithm : null;
    public bool IsAvailable => Algorithm is { IsRunnable: true };
    public IReadOnlyList<AlgorithmParameter> Parameters => Algorithm?.Parameters ?? [];

    /// <summary>
    /// The combined strategy keeps the interactive subscription for the current
    /// window, but a completed background Run is the authoritative source for
    /// the trend. Detail modules use this to avoid rebuilding a full trend from
    /// only the windows the user happened to visit.
    /// </summary>
    protected AnalysisRunResponse? CompletedBackgroundFullRun =>
        Catalog.IsCombinedExecutionModeSelected &&
        Catalog.BackgroundFullRun is { Status: "completed", ResultSummary: not null } run
            ? run
            : null;

    protected bool IsServingCompletedBackgroundFullRun => CompletedBackgroundFullRun is not null;

    protected virtual void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AlgorithmListViewModel.TimeDisplay)) OnTimeDisplayChanged();
        if (args.PropertyName == nameof(AlgorithmListViewModel.SelectedAlgorithm))
        {
            RaisePropertyChanged(nameof(Algorithm));
            RaisePropertyChanged(nameof(IsAvailable));
            RaisePropertyChanged(nameof(Parameters));
        }
    }

    protected virtual void OnTimeDisplayChanged() { }
}
