using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>PSD-only result projection for the Review panel.</summary>
public sealed class PsdReviewAnalysisViewModel : ObservableObject
{
    private readonly PsdPreviewState preview = new();
    private string statusText = "PSD 尚未准备。";
    private IReadOnlyList<string> channels = [];
    private string? selectedChannel;
    private string selectedTopomapBandId = "delta";
    private string selectedView = "频谱";
    private TopomapResponse? topomap;
    private string analysisRangeText = "分析范围不可用";
    private string topomapRangeText = "分析范围不可用";

    public ObservableCollection<PsdPreviewPoint?> Points => preview.Points;
    public string ValueUnit => preview.ValueUnit;
    public bool HasResult => preview.HasPreview;
    public IReadOnlyList<string> Channels { get => channels; private set => SetProperty(ref channels, value); }
    public IReadOnlyList<PsdTopomapBandOption> TopomapBandOptions { get; } =
    [
        new("delta", "Delta · 1–4 Hz"),
        new("theta", "Theta · 4–8 Hz"),
        new("alpha", "Alpha · 8–13 Hz"),
        new("beta", "Beta · 13–30 Hz"),
        new("gamma", "Gamma · 30–50 Hz"),
    ];
    public string? SelectedChannel { get => selectedChannel; set { if (SetProperty(ref selectedChannel, value)) RaisePropertyChanged(nameof(TopomapField)); } }
    public string SelectedTopomapBandId
    {
        get => selectedTopomapBandId;
        set
        {
            if (!TopomapBandOptions.Any(option => option.Id == value)) return;
            if (!SetProperty(ref selectedTopomapBandId, value)) return;
            RaisePropertyChanged(nameof(SelectedTopomapFieldId));
            RaisePropertyChanged(nameof(TopomapField));
        }
    }
    public string SelectedView { get => selectedView; set { if (SetProperty(ref selectedView, value)) { RaisePropertyChanged(nameof(IsTopomapSelected)); RaisePropertyChanged(nameof(IsSpectrumSelected)); RaisePropertyChanged(nameof(CurrentRangeText)); } } }
    public IReadOnlyList<string> ViewOptions { get; } = ["频谱", "脑电地形图"];
    public bool IsTopomapSelected => SelectedView == "脑电地形图";
    public bool IsSpectrumSelected => !IsTopomapSelected;
    public TopomapResponse? Topomap { get => topomap; private set { if (SetProperty(ref topomap, value)) RaisePropertyChanged(nameof(TopomapField)); } }
    public TopomapColorScaleState TopomapColorScales { get; } = new();
    public string SelectedTopomapFieldId => $"psd.band-mean-density.{SelectedTopomapBandId}";
    public TopomapField? TopomapField => Topomap?.Fields.FirstOrDefault(item => item.FieldId == SelectedTopomapFieldId);
    public string CurrentRangeText => IsTopomapSelected ? topomapRangeText : analysisRangeText;

    public void SetChannels(IEnumerable<string> value)
    {
        Channels = value.ToArray();
        if (SelectedChannel is null || !Channels.Contains(SelectedChannel, StringComparer.OrdinalIgnoreCase))
            SelectedChannel = Channels.FirstOrDefault();
    }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

    public void Apply(WindowPreviewResponse? response)
    {
        preview.Clear();
        analysisRangeText = response is null ? "分析范围不可用" : FormatRange(response);
        RaisePropertyChanged(nameof(CurrentRangeText));
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
            var resultChannels = ReadChannels(dynamic);
            if (resultChannels.Count == 0) throw new InvalidOperationException("PSD 结果缺少有序通道列表。");
            if (!Channels.SequenceEqual(resultChannels, StringComparer.OrdinalIgnoreCase)) SetChannels(resultChannels);
            var channelIndex = resultChannels.ToList().FindIndex(item => string.Equals(item, SelectedChannel, StringComparison.OrdinalIgnoreCase));
            if (channelIndex < 0) throw new InvalidOperationException("显示通道不属于当前 PSD 结果。");
            PsdWindowPresenter.Apply(preview, dynamic, channelIndex);
            StatusText = preview.HasPreview
                ? string.Empty
                : "当前 PSD 窗口没有可显示的有限频谱值。";
        }
        catch (InvalidOperationException exception)
        {
            preview.Clear();
            StatusText = $"PSD 结果无法展示：{exception.Message}";
        }
        RaiseResultChanged();
    }

    public void ApplyTopomap(TopomapResponse response)
    {
        if (response.Fields.Any(field => field.FieldId != SelectedTopomapFieldId || field.Unit != "V^2/Hz"))
            throw new InvalidOperationException("PSD 地形图必须返回当前频带的平均功率谱密度（V²/Hz）。");
        Topomap = response;
        topomapRangeText = FormatRange(response.WindowStartSeconds, response.WindowEndSeconds);
        RaisePropertyChanged(nameof(TopomapField));
        RaisePropertyChanged(nameof(CurrentRangeText));
    }

    public void ClearTopomap()
    {
        TopomapColorScales.Reset();
        Topomap = null;
        topomapRangeText = "分析范围不可用";
        RaisePropertyChanged(nameof(CurrentRangeText));
    }
    public void SetStatus(string value) => StatusText = value;

    private static IReadOnlyList<string> ReadChannels(DynamicWindowResponse response)
    {
        var json = response.ScientificConfigurationJson;
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("config", out var config) &&
            config.ValueKind == System.Text.Json.JsonValueKind.Object &&
            config.TryGetProperty("channels", out var channels) &&
            channels.ValueKind == System.Text.Json.JsonValueKind.Array)
            return channels.EnumerateArray().Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String).Select(item => item.GetString()!).ToArray();
        return [];
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
        return FormatRange(start, end);
    }

    private static string FormatRange(double? start, double? end) =>
        start is double from && end is double to && double.IsFinite(from) && double.IsFinite(to) && to >= from
            ? $"{from:0.###}–{to:0.###} s"
            : "分析范围不可用";
}

public sealed record PsdTopomapBandOption(string Id, string Label);
