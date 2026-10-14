using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal interface IAlgorithmRunResultHandler
{
    string AlgorithmId { get; }
    bool UsesDynamicScalarSeries => true;
    bool SupportsNotch => false;
    bool SupportsOnDemandWindows => false;
    void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response) =>
        throw new NotSupportedException("当前模块没有按需窗口展示能力。");
    void ApplyConfigFields(IDictionary<string, object?> payload, string channel, string? secondaryChannel) { }
    AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs) => new();
    Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds);
}
