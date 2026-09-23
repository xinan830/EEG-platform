
namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// One fixed source-channel row in a montage editor. Its positive source and
/// display order belong to the channel configuration and cannot be edited here.
/// </summary>
public sealed class MontageDraftRow : ObservableObject
{
    private readonly IReadOnlyList<string> availableReferenceLabels;
    private MontageNegativeKind negativeKind;
    private string selectedNegativeLabel;
    private string selectedPairFirstLabel;
    private string selectedPairSecondLabel;
    private string averageReferenceSummary;

    public MontageDraftRow(
        string positiveLabel,
        int displayOrder,
        IReadOnlyList<string> availableNegativeLabels,
        MontageNegativeKind negativeKind,
        string? selectedNegativeLabel,
        string? selectedPairFirstLabel,
        string? selectedPairSecondLabel,
        string averageReferenceSummary,
        string specifiedPairSummary)
    {
        PositiveLabel = positiveLabel;
        DisplayOrder = displayOrder;
        availableReferenceLabels = availableNegativeLabels;
        this.negativeKind = negativeKind;
        this.selectedNegativeLabel = selectedNegativeLabel ?? string.Empty;
        this.selectedPairFirstLabel = selectedPairFirstLabel ?? string.Empty;
        this.selectedPairSecondLabel = selectedPairSecondLabel ?? string.Empty;
        this.averageReferenceSummary = averageReferenceSummary;
    }

    public string PositiveLabel { get; }

    public int DisplayOrder { get; }

    public string Name => MontageDisplay.CreateName(PositiveLabel, NegativeKind, SelectedNegativeLabel);

    public MontageNegativeKind NegativeKind
    {
        get => negativeKind;
        set
        {
            if (SetProperty(ref negativeKind, value))
            {
                RaiseReferencePropertiesChanged();
            }
        }
    }

    public bool IsChannelReference => NegativeKind == MontageNegativeKind.Channel;

    public bool IsSpecifiedPairReference => NegativeKind == MontageNegativeKind.SpecifiedPair;

    public IReadOnlyList<string> ReferenceOptions => NegativeKind == MontageNegativeKind.Channel
        ? availableReferenceLabels.Where(label => !string.Equals(label, PositiveLabel, StringComparison.OrdinalIgnoreCase)).ToArray()
        : [ReferenceDisplay];

    public IReadOnlyList<string> PairFirstOptions => availableReferenceLabels
        .Where(label => !string.Equals(label, SelectedPairSecondLabel, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public IReadOnlyList<string> PairSecondOptions => availableReferenceLabels
        .Where(label => !string.Equals(label, SelectedPairFirstLabel, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public string SelectedNegativeLabel
    {
        get => selectedNegativeLabel;
        set
        {
            if (NegativeKind == MontageNegativeKind.Channel && SetProperty(ref selectedNegativeLabel, value))
            {
                RaisePropertyChanged(nameof(Name));
                RaisePropertyChanged(nameof(ReferenceDisplay));
                RaisePropertyChanged(nameof(ReferenceSelection));
            }
        }
    }

    public string ReferenceSelection
    {
        get => ReferenceDisplay;
        set => SelectedNegativeLabel = value;
    }

    public string SelectedPairFirstLabel
    {
        get => selectedPairFirstLabel;
        set
        {
            value ??= string.Empty;
            if (!CanSelectPairValue(value, SelectedPairSecondLabel) || !SetProperty(ref selectedPairFirstLabel, value))
            {
                return;
            }
            RaisePairPropertiesChanged(nameof(PairSecondOptions));
        }
    }

    public string SelectedPairSecondLabel
    {
        get => selectedPairSecondLabel;
        set
        {
            value ??= string.Empty;
            if (!CanSelectPairValue(value, SelectedPairFirstLabel) || !SetProperty(ref selectedPairSecondLabel, value))
            {
                return;
            }
            RaisePairPropertiesChanged(nameof(PairFirstOptions));
        }
    }

    public string ReferenceDisplay => NegativeKind switch
    {
        MontageNegativeKind.OriginalHardwareReference => "REF",
        MontageNegativeKind.Mean => averageReferenceSummary,
        MontageNegativeKind.SpecifiedPair => SelectedPairLabels.Count == 2
            ? $"双参考（{string.Join("、", SelectedPairLabels)}）"
            : "请选择两个通道",
        MontageNegativeKind.Channel => string.IsNullOrWhiteSpace(SelectedNegativeLabel) ? "请选择通道" : SelectedNegativeLabel,
        _ => string.Empty,
    };

    public void UpdateReferenceSummaries(string averageValue, string pairValue)
    {
        averageReferenceSummary = averageValue;
        if (NegativeKind == MontageNegativeKind.Mean)
        {
            RaisePropertyChanged(nameof(ReferenceDisplay));
            RaisePropertyChanged(nameof(ReferenceSelection));
            RaisePropertyChanged(nameof(ReferenceOptions));
        }
    }

    public void SetSpecifiedPair(string firstLabel, string secondLabel)
    {
        if (!CanSelectPairValue(firstLabel, secondLabel) || !CanSelectPairValue(secondLabel, firstLabel))
        {
            throw new InvalidOperationException("指定双参考必须选择两个不同的有效通道。");
        }
        selectedPairFirstLabel = firstLabel;
        selectedPairSecondLabel = secondLabel;
        RaisePropertyChanged(nameof(SelectedPairFirstLabel));
        RaisePropertyChanged(nameof(SelectedPairSecondLabel));
        RaisePropertyChanged(nameof(PairFirstOptions));
        RaisePropertyChanged(nameof(PairSecondOptions));
        RaisePairPropertiesChanged();
    }

    public DerivedMontageChannel ToModel(IReadOnlyList<string> averageReferenceLabels) => new(
        Name,
        PositiveLabel,
        NegativeKind,
        NegativeKind switch
        {
            MontageNegativeKind.OriginalHardwareReference => [],
            MontageNegativeKind.Channel => [SelectedNegativeLabel],
            MontageNegativeKind.Mean => averageReferenceLabels,
            MontageNegativeKind.SpecifiedPair => SelectedPairLabels,
            _ => [],
        },
        DisplayOrder);

    private IReadOnlyList<string> SelectedPairLabels =>
        new[] { selectedPairFirstLabel, selectedPairSecondLabel }
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();

    private bool CanSelectPairValue(string value, string otherValue) =>
        !string.IsNullOrWhiteSpace(value)
        && availableReferenceLabels.Contains(value, StringComparer.OrdinalIgnoreCase)
        && !string.Equals(value, otherValue, StringComparison.OrdinalIgnoreCase);

    private void RaisePairPropertiesChanged(string? optionsProperty = null)
    {
        if (optionsProperty is not null)
        {
            RaisePropertyChanged(optionsProperty);
        }
        RaisePropertyChanged(nameof(ReferenceDisplay));
        RaisePropertyChanged(nameof(ReferenceSelection));
    }

    private void RaiseReferencePropertiesChanged()
    {
        RaisePropertyChanged(nameof(Name));
        RaisePropertyChanged(nameof(IsChannelReference));
        RaisePropertyChanged(nameof(IsSpecifiedPairReference));
        RaisePropertyChanged(nameof(ReferenceDisplay));
        RaisePropertyChanged(nameof(ReferenceSelection));
        RaisePropertyChanged(nameof(ReferenceOptions));
    }
}

public sealed record MontageNegativeKindOption(MontageNegativeKind Kind, string Label);

public sealed class AverageReferenceChannelOption : ObservableObject
{
    private bool isIncluded;

    public AverageReferenceChannelOption(string label, bool isIncluded)
    {
        Label = label;
        this.isIncluded = isIncluded;
    }

    public string Label { get; }

    public bool IsIncluded
    {
        get => isIncluded;
        set => SetProperty(ref isIncluded, value);
    }
}
