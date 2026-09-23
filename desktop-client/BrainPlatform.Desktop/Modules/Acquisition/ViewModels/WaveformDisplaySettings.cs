using System.ComponentModel;
using System.Runtime.CompilerServices;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.ViewModels;

/// <summary>
/// Shared display-only settings for live acquisition and recording review.
/// It never owns samples, device state, file reads, or playback state.
/// </summary>
public sealed class WaveformDisplaySettings : INotifyPropertyChanged
{
    private double sensitivityMicrovoltsPerMillimeter = 10;
    private double paperSpeedMillimetersPerSecond = 30;
    private HorizontalTimeScaleMode horizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed;
    private double timebaseSecondsPerScreen = 10;
    private double viewportWidthDips;
    private ScreenScaleContext screenScale = ScreenScaleContext.Nominal;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => sensitivityMicrovoltsPerMillimeter;
        set
        {
            if (value is not (5d or 10d or 20d or 50d or 100d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref sensitivityMicrovoltsPerMillimeter, value);
        }
    }

    public double PaperSpeedMillimetersPerSecond
    {
        get => paperSpeedMillimetersPerSecond;
        set
        {
            if (value is not (5d or 10d or 15d or 30d or 60d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref paperSpeedMillimetersPerSecond, value);
        }
    }

    public HorizontalTimeScaleMode HorizontalTimeScaleMode
    {
        get => horizontalTimeScaleMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref horizontalTimeScaleMode, value);
        }
    }

    public double TimebaseSecondsPerScreen
    {
        get => timebaseSecondsPerScreen;
        set
        {
            if (value is not (5d or 10d or 15d or 20d or 30d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Set(ref timebaseSecondsPerScreen, value);
        }
    }

    public double ViewportWidthDips
    {
        get => viewportWidthDips;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || Math.Abs(value - viewportWidthDips) < 0.5)
            {
                return;
            }

            Set(ref viewportWidthDips, value);
        }
    }

    public ScreenScaleContext ScreenScale
    {
        get => screenScale;
        set => Set(ref screenScale, value);
    }

    public bool IsPaperSpeedMode => HorizontalTimeScaleMode == HorizontalTimeScaleMode.PaperSpeed;

    public bool IsTimebaseMode => HorizontalTimeScaleMode == HorizontalTimeScaleMode.Timebase;

    public double DerivedPaperSpeedMillimetersPerSecond => ViewportWidthDips <= 0
        ? 0
        : ViewportWidthDips * ScreenScale.MillimetersPerDipX / TimebaseSecondsPerScreen;

    public double GetVisibleSeconds(double viewportWidthDips)
    {
        if (HorizontalTimeScaleMode == HorizontalTimeScaleMode.Timebase)
        {
            return TimebaseSecondsPerScreen;
        }

        return ScreenScaleCalculator.VisibleSeconds(
            viewportWidthDips,
            ScreenScale.MillimetersPerDipX,
            PaperSpeedMillimetersPerSecond);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(HorizontalTimeScaleMode))
        {
            OnPropertyChanged(nameof(IsPaperSpeedMode));
            OnPropertyChanged(nameof(IsTimebaseMode));
        }

        if (propertyName is nameof(ViewportWidthDips)
            or nameof(ScreenScale)
            or nameof(TimebaseSecondsPerScreen))
        {
            OnPropertyChanged(nameof(DerivedPaperSpeedMillimetersPerSecond));
        }
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
