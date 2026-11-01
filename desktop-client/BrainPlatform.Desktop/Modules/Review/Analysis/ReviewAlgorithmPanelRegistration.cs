using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Algorithm-owned configuration and presentation composed by the shared Review lifecycle.</summary>
public sealed record ReviewAlgorithmPanelRegistration(
    string AlgorithmId,
    object Configuration,
    Action<RegisteredRecording, AlgorithmCatalogItem, HistoricalMontageDescriptor?> Initialize,
    Func<double, double, JsonElement> BuildConfiguration,
    Action<WindowPreviewResponse?, double> Apply);
