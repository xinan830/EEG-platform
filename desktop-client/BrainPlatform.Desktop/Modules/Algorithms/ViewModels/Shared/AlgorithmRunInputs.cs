using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed record AlgorithmRunInputs(
    TimeRange Range, string Channel, string F4Channel, IReadOnlyCollection<string> Channels,
    double? SamplingRateHz, string LowFrequencyText, string HighFrequencyText,
    string NumeratorLowText, string NumeratorHighText, string DenominatorLowText,
    string DenominatorHighText, string NotchFrequency);

internal sealed record AlgorithmRunParameters(
    AlgorithmRunConfiguration.FrequencyBand? FrequencyBand = null,
    AlgorithmRunConfiguration.BandRatioBands? RatioBands = null,
    string? F4Channel = null,
    double? NotchHz = null);
