using System.Globalization;
using SciChart.Charting.Visuals.Axes.LabelProviders;

namespace BrainPlatform.Desktop.Views;

internal sealed class SweepTimeLabelProvider : NumericLabelProvider
{
    private double pageStartElapsedSeconds;
    private double cursorSeconds;

    public void Update(double pageStartSeconds, double currentCursorSeconds)
    {
        pageStartElapsedSeconds = pageStartSeconds;
        cursorSeconds = currentCursorSeconds;
    }

    public override string FormatLabel(IComparable dataValue)
    {
        var positionSeconds = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
        if (positionSeconds < 0 || positionSeconds > cursorSeconds + 0.000001d)
        {
            return string.Empty;
        }

        return (pageStartElapsedSeconds + positionSeconds).ToString("0", CultureInfo.InvariantCulture);
    }
}
