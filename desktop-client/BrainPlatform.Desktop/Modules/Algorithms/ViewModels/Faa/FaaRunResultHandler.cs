using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;

internal sealed class FaaRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "faa";
    public bool SupportsNotch => true;

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(inputs.F4Channel) || !inputs.Channels.Contains(inputs.F4Channel))
            throw new InvalidOperationException("请选择注册记录返回的有效 FAA F4 来源通道。");
        if (string.Equals(inputs.Channel, inputs.F4Channel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FAA 的 F3 与 F4 来源通道必须不同。");
        return new(
            FrequencyBand: AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(
                inputs.LowFrequencyText, inputs.HighFrequencyText, inputs.SamplingRateHz),
            F4Channel: inputs.F4Channel,
            NotchHz: AlgorithmRunConfiguration.ParseNotchFrequency(inputs.NotchFrequency));
    }

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetStructuredPreviewTextForModule("额叶 Alpha 不对称性由后端按 F3/F4 成对 Alpha 功率计算；WPF 仅展示后端返回的结果、通道、质量和动态窗口证据。");
        if (dynamic)
        {
            ApplyDynamicWindows(catalog, run);
            if (preservedDynamicCursorSeconds is double cursorSeconds)
                catalog.SeekDynamicPreviewSecondsForModule(cursorSeconds);
        }
        return Task.CompletedTask;
    }

    internal static void ApplyDynamicWindows(AlgorithmListViewModel catalog, AnalysisRunResponse run)
    {
        var preview = FaaResultPreview.Parse(run.ResultSummary);
        var points = preview is { IsDynamic: true } ? preview.DynamicPoints : [];
        var rows = points.Select(point => new DynamicWindowRow(point.StartSeconds, point.EndSeconds,
            AlgorithmResultFormatter.FormatWindowStateForDisplay(point.State),
            AlgorithmResultFormatter.FormatQualityReasonForDisplay(point.Quality), point.Failure));
        catalog.SetDynamicResultWindows(rows, points.FirstOrDefault(point => point.IsComplete)?.EndSeconds ?? 0);
    }
}
