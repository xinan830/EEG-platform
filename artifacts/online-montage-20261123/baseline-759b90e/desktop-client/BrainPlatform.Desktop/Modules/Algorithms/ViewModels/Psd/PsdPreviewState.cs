using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

/// <summary>PSD-only preview data. The catalog coordinates loading; this object owns the PSD projection state.</summary>
public sealed class PsdPreviewState : ObservableObject
{
    private string frequencyUnit = "Hz";
    private string valueUnit = string.Empty;
    private bool hasPreview;
    private string frequencyRangeText = string.Empty;
    private string valueRangeText = string.Empty;
    private StructuredPreviewResponse? structuredPreview;
    private DynamicWindowResponse? windowPreview;

    public ObservableCollection<PsdPreviewPoint?> Points { get; } = [];
    public ObservableCollection<PsdBandSharePoint> BandShares { get; } = [];
    public JsonElement[] DynamicFrequencies { get; set; } = [];
    public JsonElement[] DynamicRows { get; set; } = [];
    public bool DynamicIsVoltsSquaredPerHz { get; set; }
    public string FrequencyUnit { get => frequencyUnit; set => SetProperty(ref frequencyUnit, value); }
    public string ValueUnit { get => valueUnit; set => SetProperty(ref valueUnit, value); }
    public bool HasPreview { get => hasPreview; set => SetProperty(ref hasPreview, value); }
    public string FrequencyRangeText { get => frequencyRangeText; set => SetProperty(ref frequencyRangeText, value); }
    public string ValueRangeText { get => valueRangeText; set => SetProperty(ref valueRangeText, value); }
    public StructuredPreviewResponse? StructuredPreview { get => structuredPreview; set => SetProperty(ref structuredPreview, value); }
    public DynamicWindowResponse? WindowPreview { get => windowPreview; set => SetProperty(ref windowPreview, value); }

    public void Clear()
    {
        Points.Clear();
        BandShares.Clear();
        HasPreview = false;
        FrequencyUnit = "Hz";
        ValueUnit = string.Empty;
        FrequencyRangeText = string.Empty;
        ValueRangeText = string.Empty;
        StructuredPreview = null;
        WindowPreview = null;
        DynamicFrequencies = [];
        DynamicRows = [];
        DynamicIsVoltsSquaredPerHz = false;
    }
}
