using SciChart.Charting.Visuals.Axes.LabelProviders;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Formats recording-relative x values as estimated local clock time.</summary>
internal sealed class RecordingTimeLabelProvider : NumericLabelProvider
{
    public DateTimeOffset? RecordingStartUtc { get; set; }

    public override string FormatLabel(IComparable dataValue)
    {
        return RecordingStartUtc is { } startUtc
            ? RecordingClockLabelFormatter.FormatAlignedSecond(startUtc, Convert.ToDouble(dataValue))
            : string.Empty;
    }
}
