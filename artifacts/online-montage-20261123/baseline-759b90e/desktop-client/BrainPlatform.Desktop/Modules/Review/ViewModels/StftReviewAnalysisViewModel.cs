namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Publishes validated windows for the chart to append without duplicating overlapping times.</summary>
public sealed class StftReviewAnalysisViewModel : ObservableObject
{
    private StftPreview? preview;
    private string statusText = "STFT 尚未准备。";
    private string currentWindowRangeText = "分析范围不可用";
    public StftPreview? Preview { get => preview; private set => SetProperty(ref preview, value); }
    public bool HasResult => Preview is not null;
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string CurrentWindowRangeText { get => currentWindowRangeText; private set => SetProperty(ref currentWindowRangeText, value); }
    public void Apply(WindowPreviewResponse? response)
    {
        StftPreview? nextPreview = null;
        CurrentWindowRangeText = FormatRange(response?.WindowStartSeconds, response?.WindowEndSeconds);
        StatusText = response is null ? "STFT 尚未准备。" :
            $"{response.Status} · {string.Join("、", response.ReasonCodes)}";
        if (response?.Status == "available")
        {
            try
            {
                nextPreview = StftPreview.FromReviewWindow(response);
                StatusText = "";
            }
            catch (InvalidOperationException exception)
            {
                StatusText = $"STFT 结果无法展示：{exception.Message}";
            }
        }
        Preview = nextPreview;
        RaisePropertyChanged(nameof(HasResult));
    }

    private static string FormatRange(double? start, double? end) =>
        start is double from && end is double to && double.IsFinite(from) && double.IsFinite(to) && from >= 0 && to > from
            ? $"{from:0.###}–{to:0.###} s"
            : "分析范围不可用";
}
