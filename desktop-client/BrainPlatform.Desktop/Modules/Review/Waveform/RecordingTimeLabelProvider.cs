using System.Globalization;
using SciChart.Charting.Visuals.Axes.LabelProviders;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Formats recording-relative x values as elapsed seconds.</summary>
internal sealed class RecordingTimeLabelProvider : NumericLabelProvider
{
    public override string FormatLabel(IComparable dataValue)
    {
        var elapsedSeconds = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
        return elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
