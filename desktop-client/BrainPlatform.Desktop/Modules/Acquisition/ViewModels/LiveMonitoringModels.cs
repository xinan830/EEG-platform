using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

internal sealed record ConfiguredDisplayChannel(string Label, string Kind, bool IsSelectedForDisplay);

public sealed record LiveWaveformSource(
    AcquisitionStreamMetadata Metadata,
    IReadOnlyList<AcquisitionBatch> Batches,
    IReadOnlyList<LiveDisplayChannel> Channels,
    long? SessionFirstSampleCounter = null,
    IReadOnlyList<LiveDisplayCounterAdjustment>? DisplayCounterAdjustments = null,
    IReadOnlyList<long>? DisplayFilterBoundaries = null,
    MontageProfile? MontageProfile = null,
    IReadOnlyList<LiveDisplayChannel>? MontageSourceChannels = null,
    IReadOnlySet<string>? VisibleMontageChannelNames = null);

public sealed record LiveDisplayCounterAdjustment(
    long EffectiveFromRawSampleCounter,
    long SkippedSampleCountBefore);

public sealed class LiveDisplayChannel : ObservableObject
{
    private bool isVisible;

    public LiveDisplayChannel(int streamIndex, int nativeChannelIndex, string label, string kind, bool isVisible)
    {
        StreamIndex = streamIndex;
        NativeChannelIndex = nativeChannelIndex;
        Label = label;
        Kind = kind;
        this.isVisible = isVisible;
    }

    public int StreamIndex { get; }

    public int NativeChannelIndex { get; }

    public string Label { get; }

    public string Kind { get; }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }
}

public sealed class LiveMontageDisplayChannel : ObservableObject
{
    private bool isVisible;

    public LiveMontageDisplayChannel(string name, int displayOrder, bool isVisible)
    {
        Name = name;
        DisplayOrder = displayOrder;
        this.isVisible = isVisible;
    }

    public string Name { get; }

    public int DisplayOrder { get; }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }
}
