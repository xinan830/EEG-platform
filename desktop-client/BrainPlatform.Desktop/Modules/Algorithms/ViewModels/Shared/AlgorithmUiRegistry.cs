using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed record AlgorithmUiDescriptor(
    string Id,
    AlgorithmDetailKind DetailKind,
    string ChannelLabel,
    Func<AlgorithmListViewModel, object> CreateModule,
    string? PreferredPrimaryChannel = null,
    string? PreferredSecondaryChannel = null,
    string? DefaultPeakBandPreset = null);

/// <summary>
/// Desktop composition metadata.  Algorithm modules own their behavior; this
/// registry only maps catalog IDs to their detail module and display metadata.
/// Keep this as the single UI mapping instead of adding ID switches to views.
/// </summary>
internal static class AlgorithmUiRegistry
{
    private static readonly IReadOnlyDictionary<string, AlgorithmUiDescriptor> Descriptors =
        new Dictionary<string, AlgorithmUiDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["psd"] = new("psd", AlgorithmDetailKind.FrequencySpectrum, "PSD 通道", catalog => new PsdDetailViewModel(catalog)),
            ["stft"] = new("stft", AlgorithmDetailKind.TimeFrequency, "STFT 通道", catalog => new StftDetailViewModel(catalog)),
            ["rbp"] = new("rbp", AlgorithmDetailKind.RelativeBandPower, "RBP 通道", catalog => new RbpDetailViewModel(catalog)),
            ["faa"] = new("faa", AlgorithmDetailKind.Faa, "FAA F3 通道", catalog => new FaaDetailViewModel(catalog), "F3", "F4"),
            ["iapf"] = new("iapf", AlgorithmDetailKind.Iapf, "IAPF 通道", catalog => new IapfDetailViewModel(catalog)),
            ["peak_frequency"] = new("peak_frequency", AlgorithmDetailKind.PeakFrequency, "峰频率通道", catalog => new PeakFrequencyDetailViewModel(catalog), DefaultPeakBandPreset: "Alpha（8–13 Hz）"),
            ["band_ratio"] = new("band_ratio", AlgorithmDetailKind.BandRatio, "频段比通道", catalog => new BandRatioDetailViewModel(catalog)),
            ["theta_beta"] = new("theta_beta", AlgorithmDetailKind.ThetaBeta, "Theta/Beta 通道", catalog => new ThetaBetaDetailViewModel(catalog)),
            ["brainbeat"] = new("brainbeat", AlgorithmDetailKind.Brainbeat, "Brainbeat Fz 通道", catalog => new BrainbeatDetailViewModel(catalog), "Fz", "Pz"),
        };

    internal static bool TryGet(string? algorithmId, out AlgorithmUiDescriptor descriptor)
    {
        if (algorithmId is not null && Descriptors.TryGetValue(algorithmId, out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = new AlgorithmUiDescriptor(string.Empty, AlgorithmDetailKind.Scalar, "分析通道", _ => new object());
        return false;
    }

    internal static AlgorithmUiDescriptor For(string algorithmId) =>
        TryGet(algorithmId, out var descriptor)
            ? descriptor
            : new AlgorithmUiDescriptor(algorithmId, AlgorithmDetailKind.Scalar, "分析通道", _ => new object());

    internal static IReadOnlyDictionary<string, object> CreateModules(AlgorithmListViewModel catalog) =>
        Descriptors.Values.ToDictionary(item => item.Id, item => item.CreateModule(catalog), StringComparer.OrdinalIgnoreCase);
}
