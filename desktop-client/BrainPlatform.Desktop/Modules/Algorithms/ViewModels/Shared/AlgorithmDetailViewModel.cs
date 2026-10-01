namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed class AlgorithmDetailViewModel : ObservableObject
{
    private AlgorithmCatalogItem? algorithm;
    private object? currentModule;
    private readonly IReadOnlyDictionary<string, object> modules;

    internal AlgorithmDetailViewModel(AlgorithmListViewModel catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
        algorithm = Catalog.SelectedAlgorithm;
        Context = new AnalysisContextViewModel(Catalog);
        modules = AlgorithmUiRegistry.CreateModules(Catalog);
        Psd = (PsdDetailViewModel)modules["psd"];
        Stft = (StftDetailViewModel)modules["stft"];
        Rbp = (RbpDetailViewModel)modules["rbp"];
        Faa = (FaaDetailViewModel)modules["faa"];
        Iapf = (IapfDetailViewModel)modules["iapf"];
        PeakFrequency = (PeakFrequencyDetailViewModel)modules["peak_frequency"];
        BandRatio = (BandRatioDetailViewModel)modules["band_ratio"];
        ThetaBeta = (ThetaBetaDetailViewModel)modules["theta_beta"];
        Brainbeat = (BrainbeatDetailViewModel)modules["brainbeat"];
        Quality = new QualityStatusViewModel(Catalog, modules.Values.OfType<IAlgorithmQualitySource>().ToArray());
        currentModule = Scalar;
    }

    public AlgorithmListViewModel Catalog { get; }
    public AnalysisContextViewModel Context { get; }
    public PsdDetailViewModel Psd { get; }
    public StftDetailViewModel Stft { get; }
    public QualityStatusViewModel Quality { get; }
    public RbpDetailViewModel Rbp { get; }
    public FaaDetailViewModel Faa { get; }
    public IapfDetailViewModel Iapf { get; }
    public PeakFrequencyDetailViewModel PeakFrequency { get; }
    public BandRatioDetailViewModel BandRatio { get; }
    public ThetaBetaDetailViewModel ThetaBeta { get; }
    public BrainbeatDetailViewModel Brainbeat { get; }
    public object? CurrentModule
    {
        get => currentModule;
        private set => SetProperty(ref currentModule, value);
    }

    private object Scalar => Brainbeat;

    public AlgorithmCatalogItem? Algorithm
    {
        get => algorithm;
        private set
        {
            if (!SetProperty(ref algorithm, value)) return;
            RaisePropertyChanged(nameof(HasAlgorithm));
            RaisePropertyChanged(nameof(DetailKind));
            RaisePropertyChanged(nameof(IsScalar));
            RaisePropertyChanged(nameof(IsFrequencySpectrum));
            RaisePropertyChanged(nameof(IsTimeFrequency));
            RaisePropertyChanged(nameof(IsRelativeBandPower));
            RaisePropertyChanged(nameof(IsPsd));
            RaisePropertyChanged(nameof(IsStft));
            RaisePropertyChanged(nameof(IsRbp));
            RaisePropertyChanged(nameof(IsFaa));
            RaisePropertyChanged(nameof(IsIapf));
            RaisePropertyChanged(nameof(IsPeakFrequency));
            RaisePropertyChanged(nameof(IsBandRatio));
            RaisePropertyChanged(nameof(IsThetaBeta));
            RaisePropertyChanged(nameof(IsBrainbeat));
            CurrentModule = Algorithm is not null && modules.TryGetValue(Algorithm.Id, out var module)
                ? module
                : Scalar;
        }
    }

    public bool HasAlgorithm => Algorithm is not null;
    public AlgorithmDetailKind DetailKind => Algorithm is null
        ? AlgorithmDetailKind.Scalar
        : AlgorithmDetailKindResolver.Resolve(Algorithm);
    public bool IsScalar => DetailKind == AlgorithmDetailKind.Scalar;
    public bool IsFrequencySpectrum => DetailKind == AlgorithmDetailKind.FrequencySpectrum;
    public bool IsTimeFrequency => DetailKind == AlgorithmDetailKind.TimeFrequency;
    public bool IsRelativeBandPower => DetailKind == AlgorithmDetailKind.RelativeBandPower;
    public bool IsPsd => DetailKind == AlgorithmDetailKind.FrequencySpectrum;
    public bool IsStft => DetailKind == AlgorithmDetailKind.TimeFrequency;
    public bool IsRbp => DetailKind == AlgorithmDetailKind.RelativeBandPower;
    public bool IsFaa => DetailKind == AlgorithmDetailKind.Faa;
    public bool IsIapf => DetailKind == AlgorithmDetailKind.Iapf;
    public bool IsPeakFrequency => DetailKind == AlgorithmDetailKind.PeakFrequency;
    public bool IsBandRatio => DetailKind == AlgorithmDetailKind.BandRatio;
    public bool IsThetaBeta => DetailKind == AlgorithmDetailKind.ThetaBeta;
    public bool IsBrainbeat => DetailKind == AlgorithmDetailKind.Brainbeat;

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AlgorithmListViewModel.SelectedAlgorithm))
            Algorithm = Catalog.SelectedAlgorithm;
    }
}
