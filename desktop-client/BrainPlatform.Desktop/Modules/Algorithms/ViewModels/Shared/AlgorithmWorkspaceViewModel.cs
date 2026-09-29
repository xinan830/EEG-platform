using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed class AlgorithmWorkspaceViewModel : ObservableObject
{
    private bool isDetailOpen;

    public AlgorithmWorkspaceViewModel(AlgorithmListViewModel catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Detail = new AlgorithmDetailViewModel(Catalog);
        // The row supplies the algorithm as CommandParameter, so the action
        // must remain enabled even before ListView.SelectedItem is updated.
        OpenDetailCommand = new ParameterizedCommand(OpenDetail, () => true);
        BackToListCommand = new RelayCommand(() => IsDetailOpen = false);
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
    }

    public AlgorithmListViewModel Catalog { get; }
    public AlgorithmDetailViewModel Detail { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand BackToListCommand { get; }

    public bool IsDetailOpen
    {
        get => isDetailOpen;
        private set
        {
            if (!SetProperty(ref isDetailOpen, value)) return;
            RaisePropertyChanged(nameof(IsListOpen));
        }
    }

    public bool IsListOpen => !IsDetailOpen;

    public void OpenSelectedAlgorithm()
    {
        if (Catalog.SelectedAlgorithm is null)
            throw new InvalidOperationException("请先选择一个算法。");
        IsDetailOpen = true;
    }

    private void OpenDetail(object? parameter)
    {
        if (parameter is AlgorithmCatalogItem algorithm)
            Catalog.SelectedAlgorithm = algorithm;
        OpenSelectedAlgorithm();
    }

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AlgorithmListViewModel.SelectedAlgorithm))
            (OpenDetailCommand as ParameterizedCommand)?.RaiseCanExecuteChanged();
    }

    private sealed class ParameterizedCommand(Action<object?> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
