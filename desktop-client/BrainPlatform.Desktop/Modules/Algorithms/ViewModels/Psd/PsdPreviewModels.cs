namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

/// <summary>Backend-projected PSD point. WPF does not calculate scientific values.</summary>
public sealed record PsdPreviewPoint(double Frequency, double Value);

/// <summary>One backend-provided relative band-power value for PSD presentation.</summary>
public sealed record PsdBandSharePoint(
    string Name,
    string Range,
    double Share,
    string ShareText,
    string Color,
    bool IsAvailable = true);
