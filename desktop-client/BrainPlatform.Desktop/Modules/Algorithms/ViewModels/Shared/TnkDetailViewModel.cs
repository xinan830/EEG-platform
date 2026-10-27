using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed partial class TnkDetailViewModel : AlgorithmSpecificDetailViewModel
{
    internal TnkDetailViewModel(AlgorithmListViewModel catalog, string algorithmId)
        : base(catalog, algorithmId)
    {
        InitializeWorkbench();
    }

    public string StatusText => Algorithm?.StatusDisplay ?? "未选择";

    public string GroupText => Algorithm?.Group ?? "TNK 脑指标";

    public string OutputText
    {
        get
        {
            if (Algorithm is null) return "暂无输出契约";
            if (Algorithm.OutputSchema.ValueKind != JsonValueKind.Object) return "输出契约由后端提供";
            return Algorithm.OutputSchema.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array
                ? $"后端声明 {fields.GetArrayLength()} 个结果字段"
                : "输出契约由后端提供";
        }
    }

    public IReadOnlyList<TnkChildDefinition> ChildModules =>
        Algorithm is null ? [] : TnkGroupResultProjection.ReadDefinitions(Algorithm.OutputSchema);

    private IReadOnlyList<TnkChildResultDisplay>? childResults;
    private readonly TnkWindowChartHistory windowHistory = new();
    public IReadOnlyList<TnkChildResultDisplay> ChildResults => childResults ??= BuildResults();

    private IReadOnlyList<TnkChildResultDisplay> BuildResults()
    {
        if (Algorithm is null) return [];
        if (CompletedBackgroundFullRun is { } completed)
            return TnkResultChartProjection.ReadRun(completed, Algorithm.OutputSchema);
        if (Catalog.IsOnDemandAnalysis && Catalog.DisplayedAnalysisWindow is not null)
            return windowHistory.Read(Catalog.DisplayedAnalysisWindow,
                TnkResultChartProjection.ReadWindow(Catalog.DisplayedAnalysisWindow, Algorithm.OutputSchema), Catalog.SelectedDynamicStepSeconds);
        if (Catalog.IsOnDemandAnalysis)
            return ChildModules.Select(child => new TnkChildResultDisplay(child.AlgorithmId, child.DisplayName,
                child.RequiredRolesText, "等待当前目标", "尚未计算", "等待结果", "当前目标返回后显示数值与图表。")).ToArray();
        var results = TnkResultChartProjection.ReadRun(Catalog.LastRun?.RunId == withheldRunId ? null : Catalog.LastRun, Algorithm.OutputSchema);
        return results.Count > 0 ? results : ChildModules.Select(child => new TnkChildResultDisplay(
            child.AlgorithmId, child.DisplayName, child.RequiredRolesText, "运行后显示", "尚未计算",
            "尚未计算", "运行 TNK 脑指标组后显示本模块的数值和图表。")).ToArray();
    }

    public string ResultContextText => Catalog.IsOnDemandAnalysis
        ? Catalog.DisplayedAnalysisWindow?.Range is { } range
            ? $"按需窗口：{range.StartSeconds:G6}–{range.EndSeconds:G6} s · {Catalog.DisplayedAnalysisWindow.State}"
            : "按需分析：等待当前目标结果"
        : Catalog.LastRun is { } run
            ? $"Run：{run.RunId} · {run.Status}；数值为最后返回窗口，趋势图显示已返回历史。"
            : "一次配置来源，统一运行；以下按模块展示后端返回结果。";

    public string AvailabilityText => Algorithm?.IsRunnable == true
        ? "可运行"
        : "科学 parity 与运行模块验收完成前不可运行";

    protected override void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnCatalogPropertyChanged(sender, args);
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm) or nameof(AlgorithmListViewModel.IsOnDemandAnalysis))
            windowHistory.Clear();
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm) or
            nameof(AlgorithmListViewModel.LastRun) or nameof(AlgorithmListViewModel.DisplayedAnalysisWindow) or
            nameof(AlgorithmListViewModel.IsOnDemandAnalysis) or nameof(AlgorithmListViewModel.BackgroundFullRun))
        {
            var retainProjectedHistory = args.PropertyName == nameof(AlgorithmListViewModel.DisplayedAnalysisWindow)
                && Catalog.IsOnDemandAnalysis && workbenchActive && childResults is not null;
            if (!retainProjectedHistory)
            {
                childResults = null;
                RaisePropertyChanged(nameof(ChildResults));
            }
            RaisePropertyChanged(nameof(ResultContextText));
            RefreshSelectedMetric();
            summaryTarget = "";
            RefreshScoreStates();
        }
        HandleWaveformContextChange(args.PropertyName);
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm))
        {
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(OutputText));
            RaisePropertyChanged(nameof(ChildModules));
            RaisePropertyChanged(nameof(ChildResults));
        }
    }
}
