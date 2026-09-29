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

    protected virtual void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AlgorithmListViewModel.SelectedAlgorithm))
        {
            RaisePropertyChanged(nameof(Algorithm));
            RaisePropertyChanged(nameof(IsAvailable));
            RaisePropertyChanged(nameof(Parameters));
        }
    }
}
