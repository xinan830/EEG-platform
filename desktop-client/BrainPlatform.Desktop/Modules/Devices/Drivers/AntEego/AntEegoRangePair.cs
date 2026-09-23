namespace BrainPlatform.Desktop.Acquisition.AntEego;

/// <summary>
/// ANT/eego requires the auxiliary (bipolar) input range to be 2.5 times the
/// reference input range when opening an EEG stream. This is validated before
/// the native call so a rejected stream is never treated as a capture fault.
/// </summary>
public static class AntEegoRangePair
{
    private const double BipolarToReferenceRatio = 2.5d;

    public static bool IsCompatible(double referenceRangeVolts, double bipolarRangeVolts)
    {
        if (referenceRangeVolts <= 0 || bipolarRangeVolts <= 0)
        {
            return false;
        }

        var expected = referenceRangeVolts * BipolarToReferenceRatio;
        return Math.Abs(bipolarRangeVolts - expected) <= Math.Max(1e-12d, expected * 1e-9d);
    }

    public static IReadOnlyList<double> GetCompatibleBipolarRanges(
        double referenceRangeVolts,
        IEnumerable<double> availableBipolarRanges) =>
        availableBipolarRanges.Where(range => IsCompatible(referenceRangeVolts, range)).ToArray();

    public static (double ReferenceRangeVolts, double BipolarRangeVolts)? FindFirstCompatiblePair(
        IEnumerable<double> availableReferenceRanges,
        IEnumerable<double> availableBipolarRanges)
    {
        var bipolarRanges = availableBipolarRanges.ToArray();
        foreach (var referenceRange in availableReferenceRanges)
        {
            var bipolarRange = bipolarRanges.FirstOrDefault(range => IsCompatible(referenceRange, range));
            if (bipolarRange > 0)
            {
                return (referenceRange, bipolarRange);
            }
        }

        return null;
    }

    public static void Validate(double referenceRangeVolts, double bipolarRangeVolts)
    {
        if (!IsCompatible(referenceRangeVolts, bipolarRangeVolts))
        {
            throw new ArgumentException(
                "ANT/eego 要求双极量程必须为参考量程的 2.5 倍。",
                nameof(bipolarRangeVolts));
        }
    }
}
