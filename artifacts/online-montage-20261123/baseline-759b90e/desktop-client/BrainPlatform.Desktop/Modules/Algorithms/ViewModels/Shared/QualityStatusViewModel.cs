using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed class QualityStatusViewModel : ObservableObject
{
    private readonly AlgorithmListViewModel catalog;
    private readonly IReadOnlyDictionary<string, IAlgorithmQualitySource> sources;

    internal QualityStatusViewModel(AlgorithmListViewModel catalog,
        IEnumerable<IAlgorithmQualitySource> sources)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        // Detail modules are constructed during startup. Keep the shared card
        // resilient to an optional module being unavailable in an older build.
        this.sources = sources
            .Where(source => source is not null)
            .GroupBy(source => source!.AlgorithmId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        catalog.PropertyChanged += OnCatalogChanged;
        foreach (var source in this.sources.Values)
            source.PropertyChanged += OnSourceChanged;
    }

    private IAlgorithmQualitySource? CurrentSource =>
        catalog.SelectedAlgorithm?.Id is { } id && sources.TryGetValue(id, out var source)
            ? source
            : null;

    public bool IsAvailable => CurrentSource?.IsAvailable == true || (catalog.SelectedAlgorithm?.IsRunnable == true && catalog.LastRun is not null);
    public string QualityText => CurrentSource?.QualityText ?? GenericQualityText();
    public int UnavailableWindowCount => CurrentSource?.UnavailableWindowCount ?? GenericWindowCount("Unavailable");
    public int RejectedWindowCount => CurrentSource?.RejectedWindowCount ?? GenericWindowCount("Rejected");
    public string WindowStateSummary => CurrentSource?.WindowStateSummary ?? GenericWindowSummary();
    public string FailureReasonText => CurrentSource?.FailureReasonText ?? GenericFailureReason();
    public string FailureDetailsText => CurrentSource?.FailureDetailsText ?? GenericFailureDetails();
    public bool IsFailureDetailsExpanded => CurrentSource?.IsFailureDetailsExpanded == true;
    public ICommand ToggleFailureDetailsCommand => CurrentSource?.ToggleFailureDetailsCommand ?? NoopCommand.Instance;

    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm) or nameof(AlgorithmListViewModel.LastRun))
            RaiseAll();
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is IAlgorithmQualitySource source &&
            string.Equals(source.AlgorithmId, catalog.SelectedAlgorithm?.Id, StringComparison.OrdinalIgnoreCase))
            RaiseAll();
    }

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(IsAvailable));
        RaisePropertyChanged(nameof(QualityText));
        RaisePropertyChanged(nameof(UnavailableWindowCount));
        RaisePropertyChanged(nameof(RejectedWindowCount));
        RaisePropertyChanged(nameof(WindowStateSummary));
        RaisePropertyChanged(nameof(FailureReasonText));
        RaisePropertyChanged(nameof(FailureDetailsText));
        RaisePropertyChanged(nameof(IsFailureDetailsExpanded));
        RaisePropertyChanged(nameof(ToggleFailureDetailsCommand));
    }

    private JsonElement? Metric
    {
        get
        {
            var summary = catalog.LastRun?.ResultSummary;
            return summary is { ValueKind: JsonValueKind.Object } value &&
                   value.TryGetProperty("metric", out var metric) ? metric : null;
        }
    }

    private string GenericQualityText()
    {
        if (catalog.LastRun is null) return "尚未运行分析";
        if (catalog.LastRun.Error is not null) return "运行失败";
        var metric = Metric;
        if (metric is not { ValueKind: JsonValueKind.Object } value) return "未提供";
        if (value.TryGetProperty("quality", out var quality))
            return AlgorithmResultFormatter.FormatQualityForDisplay(quality);
        if (value.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var points = series.EnumerateArray().ToArray();
            var latest = points.LastOrDefault();
            return latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("quality", out var latestQuality)
                ? AlgorithmResultFormatter.FormatQualityForDisplay(latestQuality) : "未提供";
        }
        return "未提供";
    }

    private int GenericWindowCount(string state)
    {
        var series = Metric?.TryGetProperty("series", out var value) == true && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
        return series.Count(point => point.TryGetProperty("analysis_state", out var item) &&
                                     string.Equals(item.GetString(), state, StringComparison.OrdinalIgnoreCase));
    }

    private string GenericWindowSummary()
    {
        var series = Metric?.TryGetProperty("series", out var value) == true && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray() : [];
        if (series.Length == 0) return catalog.LastRun is null ? "未选择算法" : "静态分析";
        return string.Join("、", series.GroupBy(point => point.TryGetProperty("analysis_state", out var item) ? item.GetString() ?? "未知" : "未知")
            .Select(group => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(group.Key)}：{group.Count()}"));
    }

    private string GenericFailureReason()
    {
        if (catalog.LastRun?.Error is { } error) return error.Message;
        var failures = GenericFailureElements().ToArray();
        return failures.Length == 0 ? "暂无失败原因" : AlgorithmResultFormatter.FormatFailureCodeForDisplay(failures[^1]);
    }

    private string GenericFailureDetails()
    {
        if (catalog.LastRun?.Error is { } error) return error.Message;
        var metric = Metric;
        if (metric is not { ValueKind: JsonValueKind.Object } value) return "暂无失败详情";
        if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
            return failure.TryGetProperty("message", out var message) ? message.GetString() ?? "暂无失败详情" : "暂无失败详情";
        var series = value.TryGetProperty("series", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : Enumerable.Empty<JsonElement>();
        var lines = series.Select(point =>
        {
            var failed = point.TryGetProperty("failure", out var item) && item.ValueKind == JsonValueKind.Object;
            if (!failed) return null;
            var start = point.TryGetProperty("window_start_s", out var startValue) ? startValue.ToString() : "?";
            var end = point.TryGetProperty("window_end_s", out var endValue) ? endValue.ToString() : "?";
            var message = point.TryGetProperty("failure", out var failureValue) && failureValue.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : null;
            return $"{start}–{end} s：{message ?? "窗口不可用"}";
        }).Where(line => line is not null).ToArray();
        return lines.Length == 0 ? "暂无失败详情" : string.Join(Environment.NewLine, lines!);
    }

    private IEnumerable<string> GenericFailureElements()
    {
        var metric = Metric;
        if (metric is not { ValueKind: JsonValueKind.Object } value) yield break;
        if (value.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object && failure.TryGetProperty("code", out var code))
            yield return code.GetString() ?? "";
        if (value.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
            foreach (var point in series.EnumerateArray())
                if (point.TryGetProperty("failure", out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("code", out var pointCode))
                    yield return pointCode.GetString() ?? "";
    }

    private sealed class NoopCommand : ICommand
    {
        internal static readonly NoopCommand Instance = new();
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) { }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
