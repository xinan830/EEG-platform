using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>PSD-only result projection for the Review panel.</summary>
public sealed class PsdReviewAnalysisViewModel : ObservableObject
{
    private readonly PsdPreviewState preview = new();
    private string statusText = "PSD 尚未准备。";

    public ObservableCollection<PsdPreviewPoint?> Points => preview.Points;
    public string ValueUnit => preview.ValueUnit;
    public bool HasResult => preview.HasPreview;
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

    public void Apply(WindowPreviewResponse? response)
    {
        preview.Clear();
        if (response is null)
        {
            StatusText = "PSD 尚未准备。";
            RaiseResultChanged();
            return;
        }

        if (response.Status != "available")
        {
            StatusText = $"{response.Status} · {string.Join("、", response.ReasonCodes)} · " +
                FormatRange(response);
            RaiseResultChanged();
            return;
        }

        try
        {
            var dynamic = response.DynamicResponse ??
                throw new InvalidOperationException("缺少当前按需窗口响应。");
            PsdWindowPresenter.Apply(preview, dynamic);
            StatusText = preview.HasPreview
                ? $"{FormatRange(response)} · {preview.ValueUnit}"
                : "当前 PSD 窗口没有可显示的有限频谱值。";
        }
        catch (InvalidOperationException exception)
        {
            preview.Clear();
            StatusText = $"PSD 结果无法展示：{exception.Message}";
        }
        RaiseResultChanged();
    }

    private void RaiseResultChanged()
    {
        RaisePropertyChanged(nameof(HasResult));
        RaisePropertyChanged(nameof(ValueUnit));
        RaisePropertyChanged(nameof(Points));
    }

    private static string FormatRange(WindowPreviewResponse response)
    {
        var start = response.WindowStartSeconds ?? response.DynamicResponse?.Range?.StartSeconds;
        var end = response.WindowEndSeconds ?? response.DynamicResponse?.Range?.EndSeconds;
        return start is double from && end is double to
            ? $"{from:0.###}–{to:0.###} s"
            : "实际窗口范围不可用";
    }
}
