using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal sealed record FullRunMetricWindowSpec(
    string ValueField,
    IReadOnlyList<string>? MultiMetricFields = null);

internal interface IAlgorithmRunResultHandler
{
    string AlgorithmId { get; }
    bool UsesDynamicScalarSeries => true;
    bool SupportsNotch => false;
    bool SupportsOnDemandWindows => false;
    bool SupportsFullRunWindowPreview => false;
    FullRunMetricWindowSpec? FullRunMetricWindowSpec => null;
    bool SupportsFullRunMetricWindowPreview => FullRunMetricWindowSpec is not null;
    bool TryAdaptFullRunWindow(
        AnalysisRunResponse run,
        DynamicWindowRequest target,
        double? samplingRateHz,
        out DynamicWindowResponse response)
    {
        if (FullRunMetricWindowSpec is { } spec)
            return FullRunMetricWindowPreviewAdapter.TryCreate(
                run, target, samplingRateHz, spec.ValueField, spec.MultiMetricFields, out response);
        response = null!;
        return false;
    }
    void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response) =>
        throw new NotSupportedException("当前模块没有按需窗口展示能力。");
    void ApplyConfigFields(IDictionary<string, object?> payload, string channel, string? secondaryChannel) { }
    void ApplyConfigFields(IDictionary<string, object?> payload, string channel, string? secondaryChannel,
        IReadOnlyDictionary<string, string>? roleChannels) => ApplyConfigFields(payload, channel, secondaryChannel);
    AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs) => new();
    Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds);
}
