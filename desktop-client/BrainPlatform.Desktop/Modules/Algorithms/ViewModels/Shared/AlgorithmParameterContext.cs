using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>
/// Parameter boundary consumed by shared analysis controls.
/// The catalog remains the run-state owner for now, while this context keeps
/// shared XAML independent from catalog implementation details.
/// </summary>
public sealed class AlgorithmParameterContext : ObservableObject
{
    private readonly AlgorithmListViewModel catalog;

    internal AlgorithmParameterContext(AlgorithmListViewModel catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        catalog.PropertyChanged += OnCatalogPropertyChanged;
    }

    public string LowFrequencyText { get => catalog.LowFrequencyText; set => catalog.LowFrequencyText = value; }
    public string HighFrequencyText { get => catalog.HighFrequencyText; set => catalog.HighFrequencyText = value; }
    public IReadOnlyList<string> NotchFrequencyOptions => catalog.NotchFrequencyOptions;
    public string SelectedNotchFrequency { get => catalog.SelectedNotchFrequency; set => catalog.SelectedNotchFrequency = value; }
    public string NumeratorLowFrequencyText { get => catalog.NumeratorLowFrequencyText; set => catalog.NumeratorLowFrequencyText = value; }
    public string NumeratorHighFrequencyText { get => catalog.NumeratorHighFrequencyText; set => catalog.NumeratorHighFrequencyText = value; }
    public string DenominatorLowFrequencyText { get => catalog.DenominatorLowFrequencyText; set => catalog.DenominatorLowFrequencyText = value; }
    public string DenominatorHighFrequencyText { get => catalog.DenominatorHighFrequencyText; set => catalog.DenominatorHighFrequencyText = value; }
    public bool IsBandRatioSelected => AlgorithmUiRegistry.For(catalog.SelectedAlgorithm?.Id ?? string.Empty).HasBandRatioParameters;
    public bool IsPeakFrequencySelected => AlgorithmUiRegistry.For(catalog.SelectedAlgorithm?.Id ?? string.Empty).HasPeakFrequencyParameters;
    public IReadOnlyList<string> PeakBandPresetNames => catalog.FrequencyBandPresets.Select(item => item.Name).ToArray();
    public string SelectedPeakBandPreset { get => catalog.SelectedPeakBandPreset; set => catalog.SelectedPeakBandPreset = value; }
    public IReadOnlyList<string> BandRatioPresetNames => catalog.BandRatioPresets.Select(item => item.Name).ToArray();
    public string SelectedRatioPreset { get => catalog.SelectedRatioPreset; set => catalog.SelectedRatioPreset = value; }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(AlgorithmListViewModel.LowFrequencyText): RaisePropertyChanged(nameof(LowFrequencyText)); break;
            case nameof(AlgorithmListViewModel.HighFrequencyText): RaisePropertyChanged(nameof(HighFrequencyText)); break;
            case nameof(AlgorithmListViewModel.NotchFrequencyOptions): RaisePropertyChanged(nameof(NotchFrequencyOptions)); break;
            case nameof(AlgorithmListViewModel.SelectedNotchFrequency): RaisePropertyChanged(nameof(SelectedNotchFrequency)); break;
            case nameof(AlgorithmListViewModel.NumeratorLowFrequencyText): RaisePropertyChanged(nameof(NumeratorLowFrequencyText)); break;
            case nameof(AlgorithmListViewModel.NumeratorHighFrequencyText): RaisePropertyChanged(nameof(NumeratorHighFrequencyText)); break;
            case nameof(AlgorithmListViewModel.DenominatorLowFrequencyText): RaisePropertyChanged(nameof(DenominatorLowFrequencyText)); break;
            case nameof(AlgorithmListViewModel.DenominatorHighFrequencyText): RaisePropertyChanged(nameof(DenominatorHighFrequencyText)); break;
            case nameof(AlgorithmListViewModel.IsBandRatioSelected): RaisePropertyChanged(nameof(IsBandRatioSelected)); break;
            case nameof(AlgorithmListViewModel.IsPeakFrequencySelected): RaisePropertyChanged(nameof(IsPeakFrequencySelected)); break;
            case nameof(AlgorithmListViewModel.FrequencyBandPresets): RaisePropertyChanged(nameof(PeakBandPresetNames)); break;
            case nameof(AlgorithmListViewModel.SelectedPeakBandPreset): RaisePropertyChanged(nameof(SelectedPeakBandPreset)); break;
            case nameof(AlgorithmListViewModel.BandRatioPresets): RaisePropertyChanged(nameof(BandRatioPresetNames)); break;
            case nameof(AlgorithmListViewModel.SelectedRatioPreset): RaisePropertyChanged(nameof(SelectedRatioPreset)); break;
        }
    }
}
