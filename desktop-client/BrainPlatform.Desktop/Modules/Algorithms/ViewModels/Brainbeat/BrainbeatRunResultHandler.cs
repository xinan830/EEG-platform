using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;

internal sealed class BrainbeatRunResultHandler : IAlgorithmRunResultHandler
{
    public string AlgorithmId => "brainbeat";

    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(inputs.F4Channel) || !inputs.Channels.Contains(inputs.F4Channel))
            throw new InvalidOperationException("请选择有效的 Brainbeat Pz 来源通道。");
        if (!string.Equals(inputs.Channel, "Fz", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inputs.F4Channel, "Pz", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Brainbeat 必须选择 Fz 作为主通道、Pz 作为副通道。");
        if (string.Equals(inputs.Channel, inputs.F4Channel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Brainbeat 的 Fz 与 Pz 来源通道必须不同。");
        return new(F4Channel: inputs.F4Channel);
    }

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetStructuredPreviewTextForModule("Brainbeat 由后端按 Fz/Pz 双通道独立窗口计算，WPF 未重新计算指标。");
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
        var preview = BrainbeatResultPreview.Parse(run);
        var points = preview is { IsDynamic: true } ? preview.Points : [];
        var rows = points.Select(point =>
        {
            var end = double.IsFinite(point.EndSeconds) ? point.EndSeconds : point.TimeSeconds;
            var start = double.IsFinite(point.StartSeconds) ? point.StartSeconds : Math.Max(0, end - 1);
            return new DynamicWindowRow(
                start,
                end,
                AlgorithmResultFormatter.FormatWindowStateForDisplay(point.State),
                AlgorithmResultFormatter.FormatQualityReasonForDisplay(point.Quality),
                point.Failure);
        });
        catalog.SetDynamicResultWindows(rows, points.FirstOrDefault(point =>
            string.Equals(point.State, "Complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(point.State, "完整", StringComparison.OrdinalIgnoreCase)) is { } first
                ? (double.IsFinite(first.EndSeconds) ? first.EndSeconds : first.TimeSeconds)
                : 0);
    }
}
