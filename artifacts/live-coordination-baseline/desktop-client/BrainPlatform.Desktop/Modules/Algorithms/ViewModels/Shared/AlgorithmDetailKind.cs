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
    public static AlgorithmDetailKind Resolve(AlgorithmCatalogItem algorithm) =>
        AlgorithmUiRegistry.For(algorithm.Id).DetailKind;
}
