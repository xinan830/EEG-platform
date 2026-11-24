using SciChart.Charting.Visuals.Axes.LabelProviders;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

internal sealed class SweepTimeLabelProvider : NumericLabelProvider
{
    private double pageStartElapsedSeconds;
    private double cursorSeconds;
    private DateTimeOffset? clockAnchorUtc;

    public void Update(double pageStartSeconds, double currentCursorSeconds, DateTimeOffset? anchorUtc)
    {
        pageStartElapsedSeconds = pageStartSeconds;
        cursorSeconds = currentCursorSeconds;
        clockAnchorUtc = anchorUtc;
    }

    public override string FormatLabel(IComparable dataValue)
    {
        var positionSeconds = Convert.ToDouble(dataValue);
        if (positionSeconds < 0 || positionSeconds > cursorSeconds + 0.000001d)
        {
            return string.Empty;
        }

        return clockAnchorUtc is { } anchor
            ? RecordingClockLabelFormatter.FormatAlignedSecond(anchor, pageStartElapsedSeconds + positionSeconds)
            : string.Empty;
    }
}
