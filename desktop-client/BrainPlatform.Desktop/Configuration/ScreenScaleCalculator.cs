namespace BrainPlatform.Desktop.Configuration;

/// <summary>Pure geometry conversions shared by live acquisition and review.</summary>
public static class ScreenScaleCalculator
{
    public static double VisibleSeconds(double viewportWidthDips, double millimetersPerDipX, double paperSpeedMillimetersPerSecond)
    {
        if (!double.IsFinite(viewportWidthDips) || viewportWidthDips <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidthDips));
        if (!double.IsFinite(millimetersPerDipX) || millimetersPerDipX <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerDipX));
        if (!double.IsFinite(paperSpeedMillimetersPerSecond) || paperSpeedMillimetersPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(paperSpeedMillimetersPerSecond));
        return viewportWidthDips * millimetersPerDipX / paperSpeedMillimetersPerSecond;
    }

    public static double VerticalDisplayScale(int traceCount, double plotHeightDips, double sensitivityMicrovoltsPerMillimeter, double millimetersPerDipY)
    {
        if (traceCount <= 0) throw new ArgumentOutOfRangeException(nameof(traceCount));
        if (!double.IsFinite(plotHeightDips) || plotHeightDips <= 0) throw new ArgumentOutOfRangeException(nameof(plotHeightDips));
        if (!double.IsFinite(sensitivityMicrovoltsPerMillimeter) || sensitivityMicrovoltsPerMillimeter <= 0) throw new ArgumentOutOfRangeException(nameof(sensitivityMicrovoltsPerMillimeter));
        if (!double.IsFinite(millimetersPerDipY) || millimetersPerDipY <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerDipY));
        return traceCount / (plotHeightDips * sensitivityMicrovoltsPerMillimeter * millimetersPerDipY);
    }
}
