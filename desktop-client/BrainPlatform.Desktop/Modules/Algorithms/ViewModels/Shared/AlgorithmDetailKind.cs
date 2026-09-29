namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public enum AlgorithmDetailKind
{
    Scalar,
    FrequencySpectrum,
    TimeFrequency,
    RelativeBandPower,
    Faa,
    Iapf,
    PeakFrequency,
    BandRatio,
    ThetaBeta,
    Brainbeat,
}

internal static class AlgorithmDetailKindResolver
{
    public static AlgorithmDetailKind Resolve(AlgorithmCatalogItem algorithm) => algorithm.Id switch
    {
        "psd" => AlgorithmDetailKind.FrequencySpectrum,
        "stft" => AlgorithmDetailKind.TimeFrequency,
        "rbp" => AlgorithmDetailKind.RelativeBandPower,
        "faa" => AlgorithmDetailKind.Faa,
        "iapf" => AlgorithmDetailKind.Iapf,
        "peak_frequency" => AlgorithmDetailKind.PeakFrequency,
        "band_ratio" => AlgorithmDetailKind.BandRatio,
        "theta_beta" => AlgorithmDetailKind.ThetaBeta,
        "brainbeat" => AlgorithmDetailKind.Brainbeat,
        _ => AlgorithmDetailKind.Scalar,
    };
}
