namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed class ReviewSpectralValueRow(string channel) : ObservableObject
{
    private double? amplitudeUv;
    private double? absolutePowerUv2;
    private double? relativePowerPercent;
    private double? psdDensityUv2Hz;
    private string details = "";

    public string Channel { get; } = channel;
    public double? AmplitudeUv { get => amplitudeUv; private set => SetProperty(ref amplitudeUv, value); }
    public double? AbsolutePowerUv2 { get => absolutePowerUv2; private set => SetProperty(ref absolutePowerUv2, value); }
    public double? RelativePowerPercent { get => relativePowerPercent; private set => SetProperty(ref relativePowerPercent, value); }
    public double? PsdDensityUv2Hz { get => psdDensityUv2Hz; private set => SetProperty(ref psdDensityUv2Hz, value); }
    public string Details { get => details; private set => SetProperty(ref details, value); }

    internal void Update(double? amplitude, double? absolute, double? relative, double? density, string reason)
    {
        AmplitudeUv = amplitude;
        AbsolutePowerUv2 = absolute;
        RelativePowerPercent = relative;
        PsdDensityUv2Hz = density;
        Details = reason;
    }
}
