namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Presentation-only interpolation between two already received sweep positions.
/// It never advances beyond the latest received sample-counter position.
/// </summary>
internal sealed class SweepEraseBandAnimator
{
    private const long MinimumDurationMilliseconds = 16;
    private const long MaximumDurationMilliseconds = 80;
    private const double Epsilon = 0.000001d;
    private bool initialized;
    private double pageStartSeconds;
    private double windowSeconds;
    private double currentCursorSeconds;
    private double startCursorSeconds;
    private double targetCursorSeconds;
    private long animationStartedAtMilliseconds;
    private long animationDurationMilliseconds;

    public double SetTarget(
        double pageStart,
        double cursorSeconds,
        double windowDurationSeconds,
        long nowMilliseconds)
    {
        cursorSeconds = Math.Clamp(cursorSeconds, 0d, Math.Max(0d, windowDurationSeconds - Epsilon));
        if (!initialized ||
            Math.Abs(pageStart - pageStartSeconds) > Epsilon ||
            Math.Abs(windowDurationSeconds - windowSeconds) > Epsilon ||
            cursorSeconds + Epsilon < targetCursorSeconds)
        {
            initialized = true;
            pageStartSeconds = pageStart;
            windowSeconds = windowDurationSeconds;
            currentCursorSeconds = cursorSeconds;
            startCursorSeconds = cursorSeconds;
            targetCursorSeconds = cursorSeconds;
            animationStartedAtMilliseconds = nowMilliseconds;
            animationDurationMilliseconds = 0;
            return currentCursorSeconds;
        }

        currentCursorSeconds = Interpolate(nowMilliseconds);
        pageStartSeconds = pageStart;
        windowSeconds = windowDurationSeconds;
        startCursorSeconds = currentCursorSeconds;
        targetCursorSeconds = cursorSeconds;
        animationStartedAtMilliseconds = nowMilliseconds;
        animationDurationMilliseconds = Math.Clamp(
            (long)Math.Round(Math.Abs(targetCursorSeconds - startCursorSeconds) * 1_000d),
            MinimumDurationMilliseconds,
            MaximumDurationMilliseconds);
        return currentCursorSeconds;
    }

    public bool TryAdvance(long nowMilliseconds, out double cursorSeconds)
    {
        cursorSeconds = currentCursorSeconds;
        if (!initialized || animationDurationMilliseconds == 0)
        {
            return false;
        }

        var next = Interpolate(nowMilliseconds);
        if (Math.Abs(next - currentCursorSeconds) <= Epsilon)
        {
            return false;
        }

        currentCursorSeconds = next;
        cursorSeconds = next;
        return true;
    }

    public void Reset() => initialized = false;

    private double Interpolate(long nowMilliseconds)
    {
        if (animationDurationMilliseconds <= 0)
        {
            return targetCursorSeconds;
        }

        var progress = Math.Clamp(
            (nowMilliseconds - animationStartedAtMilliseconds) / (double)animationDurationMilliseconds,
            0d,
            1d);
        return startCursorSeconds + (targetCursorSeconds - startCursorSeconds) * progress;
    }
}
