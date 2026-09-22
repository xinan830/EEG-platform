using System.Globalization;
using System.Windows;
using System.Windows.Input;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class ScreenCalibrationViewModel : ObservableObject
{
    private readonly ScreenCalibrationStore store;
    private readonly OperationNotificationCenter notifications;
    private double screenWidthDips;
    private double screenHeightDips;
    private ScreenDisplayMetrics screenMetrics;
    private string widthCentimetersText = string.Empty;
    private string heightCentimetersText = string.Empty;
    private string statusText = "尚未完成屏幕尺寸校准。";
    private ScreenCalibrationProfile? profile;

    public ScreenCalibrationViewModel(
        ScreenCalibrationStore? store = null,
        OperationNotificationCenter? notifications = null,
        double? screenWidthDips = null)
    {
        this.store = store ?? new ScreenCalibrationStore();
        this.notifications = notifications ?? new OperationNotificationCenter();
        var primary = ScreenCalibrationMetrics.GetPrimaryScreenMetrics();
        screenMetrics = new ScreenDisplayMetrics(
            "primary-display",
            primary.WidthPixels,
            primary.HeightPixels,
            primary.Dpi / 96d,
            primary.Dpi / 96d);
        this.screenWidthDips = screenWidthDips is > 0 ? screenWidthDips.Value : screenMetrics.WidthDips;
        this.screenHeightDips = screenMetrics.HeightDips;
        DisplayKey = screenMetrics.DisplayKey;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ReportError);
        ResetCommand = new RelayCommand(Reset);
        Load();
    }

    public string DisplayKey { get; private set; }

    public string DisplayName => $"当前显示器（{DisplayKey}）";

    public string ScreenResolutionText => $"{screenMetrics.WidthPixels} × {screenMetrics.HeightPixels}";

    public string ScaleText => $"{screenMetrics.ScalePercent:0}%";

    public string DpiText => screenMetrics.DpiX.ToString("0", CultureInfo.InvariantCulture);

    public string ScreenDipSizeText => $"{screenWidthDips:0.##} × {screenHeightDips:0.##} DIP";

    public string HorizontalRulerText => $"{HorizontalRulerWidthDips:0.##} DIP";

    /// <summary>
    /// Preview conversion is derived from the width currently typed by the
    /// operator. It deliberately does not read the persisted global value:
    /// before Save that value still represents the prior calibration/default.
    /// </summary>
    public double PreviewMillimetersPerDip =>
        TryParse(WidthCentimetersText, out var width) && width > 0 && screenWidthDips > 0
            ? width * 10d / screenWidthDips
            : ScreenScaleContext.Nominal.MillimetersPerDipX;

    public string MillimetersPerDipText => $"{PreviewMillimetersPerDip:0.####} mm/DIP";

    public void UpdateDisplayContext(ScreenDisplayMetrics metrics)
    {
        if (metrics.WidthPixels <= 0 || metrics.HeightPixels <= 0)
        {
            return;
        }

        screenMetrics = metrics;
        screenWidthDips = metrics.WidthDips;
        screenHeightDips = metrics.HeightDips;
        var displayChanged = !string.Equals(DisplayKey, metrics.DisplayKey, StringComparison.OrdinalIgnoreCase);
        if (displayChanged)
        {
            DisplayKey = metrics.DisplayKey;
        }

        // Refresh the same display's conversion too: the view is created before
        // its Window handle exists, so the constructor cannot know final DPI.
        if (displayChanged)
        {
            Load();
        }
        RaisePropertyChanged(nameof(DisplayName));
        RaisePropertyChanged(nameof(ScreenResolutionText));
        RaisePropertyChanged(nameof(ScaleText));
        RaisePropertyChanged(nameof(DpiText));
        RaisePropertyChanged(nameof(ScreenDipSizeText));
        RaiseCalibrationPropertiesChanged();
    }

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public string WidthCentimetersText
    {
        get => widthCentimetersText;
        set
        {
            if (SetProperty(ref widthCentimetersText, value))
            {
                RaiseCalibrationPropertiesChanged();
            }
        }
    }

    public string HeightCentimetersText
    {
        get => heightCentimetersText;
        set
        {
            if (SetProperty(ref heightCentimetersText, value))
            {
                RaiseCalibrationPropertiesChanged();
            }
        }
    }

    public bool IsConfigured => profile?.IsValid == true;

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public double RulerWidthDips
    {
        get
        {
            return TryParse(WidthCentimetersText, out var width) && width > 0
                ? screenWidthDips * 10d / width
                : 0;
        }
    }

    public double HorizontalRulerWidthDips => RulerWidthDips;

    // The Border width includes its 8 DIP left/right padding and 1 DIP border
    // on each side. The tick grid must still occupy the full calibrated length.
    public double HorizontalRulerOuterWidthDips => HorizontalRulerWidthDips + 18d;

    public double VerticalRulerHeightDips =>
        TryParse(HeightCentimetersText, out var height) && height > 0
            ? screenHeightDips * 10d / height
            : 0;

    public double VerticalRulerOuterHeightDips => VerticalRulerHeightDips + 18d;

    public string CalibrationSummary => IsConfigured
        ? $"已校准：{profile!.WidthCentimeters:0.#} × {profile.HeightCentimeters:0.#} cm"
        : "未校准，当前使用系统名义比例";

    public async Task SaveAsync()
    {
        if (!TryParse(WidthCentimetersText, out var width) || width is < 5 or > 200)
        {
            throw new InvalidOperationException("屏幕宽度请输入 5–200 cm 之间的数值。");
        }

        if (!TryParse(HeightCentimetersText, out var height) || height is < 3 or > 150)
        {
            throw new InvalidOperationException("屏幕高度请输入 3–150 cm 之间的数值。");
        }

        profile = new ScreenCalibrationProfile(DisplayKey, width, height, DateTimeOffset.UtcNow);
        store.Save(profile);
        ScreenCalibrationMetrics.NotifyCalibrationChanged();
        StatusText = "校准已保存，采集与回溯的走纸速度已更新。";
        RaiseCalibrationPropertiesChanged();
        notifications.PublishSuccess(StatusText);
        await Task.CompletedTask;
    }

    private void Reset()
    {
        profile = null;
        WidthCentimetersText = string.Empty;
        HeightCentimetersText = string.Empty;
        ScreenCalibrationMetrics.NotifyCalibrationChanged();
        StatusText = "已恢复系统名义比例。";
        RaiseCalibrationPropertiesChanged();
    }

    private void Load()
    {
        profile = store.Load(DisplayKey);
        if (profile is null)
        {
            statusText = "尚未完成屏幕尺寸校准。";
            return;
        }

        widthCentimetersText = profile.WidthCentimeters.ToString("0.#", CultureInfo.CurrentCulture);
        heightCentimetersText = profile.HeightCentimeters.ToString("0.#", CultureInfo.CurrentCulture);
        statusText = $"已载入上次校准：{profile.WidthCentimeters:0.#} × {profile.HeightCentimeters:0.#} cm。";
    }

    private static bool TryParse(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result) && double.IsFinite(result);

    private void RaiseCalibrationPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsConfigured));
        RaisePropertyChanged(nameof(RulerWidthDips));
        RaisePropertyChanged(nameof(HorizontalRulerWidthDips));
        RaisePropertyChanged(nameof(HorizontalRulerOuterWidthDips));
        RaisePropertyChanged(nameof(VerticalRulerHeightDips));
        RaisePropertyChanged(nameof(VerticalRulerOuterHeightDips));
        RaisePropertyChanged(nameof(HorizontalRulerText));
        RaisePropertyChanged(nameof(PreviewMillimetersPerDip));
        RaisePropertyChanged(nameof(MillimetersPerDipText));
        RaisePropertyChanged(nameof(CalibrationSummary));
    }

    private void ReportError(Exception exception)
    {
        StatusText = exception.Message;
        notifications.PublishError(exception.Message);
    }
}
