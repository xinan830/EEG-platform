using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Shared.Controls.Topomap;
using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>PSD-only result projection for the Review panel.</summary>
public sealed class PsdReviewAnalysisViewModel : ObservableObject
{
    private readonly PsdPreviewState preview = new();
    private string statusText = "PSD 尚未准备。";
    private IReadOnlyList<string> channels = [];
    private IReadOnlyList<PsdFrequencyOption> frequencyOptions = [];
    private string? selectedChannel;
    private int selectedFrequencyIndex;
    private string selectedView = "频谱";
    private TopomapResponse? topomap;

    public ObservableCollection<PsdPreviewPoint?> Points => preview.Points;
    public string ValueUnit => preview.ValueUnit;
    public bool HasResult => preview.HasPreview;
    public IReadOnlyList<string> Channels { get => channels; private set => SetProperty(ref channels, value); }
    public IReadOnlyList<PsdFrequencyOption> FrequencyOptions { get => frequencyOptions; private set => SetProperty(ref frequencyOptions, value); }
    public string? SelectedChannel { get => selectedChannel; set { if (SetProperty(ref selectedChannel, value)) RaisePropertyChanged(nameof(TopomapField)); } }
    public int SelectedFrequencyIndex
    {
        get => selectedFrequencyIndex;
        set { if (SetProperty(ref selectedFrequencyIndex, value)) RaisePropertyChanged(nameof(SelectedFrequencyFieldId)); }
    }
    public string SelectedView { get => selectedView; set { if (SetProperty(ref selectedView, value)) { RaisePropertyChanged(nameof(IsTopomapSelected)); RaisePropertyChanged(nameof(IsSpectrumSelected)); } } }
    public IReadOnlyList<string> ViewOptions { get; } = ["频谱", "脑电地形图"];
    public bool IsTopomapSelected => SelectedView == "脑电地形图";
    public bool IsSpectrumSelected => !IsTopomapSelected;
    public TopomapResponse? Topomap { get => topomap; private set { if (SetProperty(ref topomap, value)) RaisePropertyChanged(nameof(TopomapField)); } }
    public TopomapColorScaleState TopomapColorScales { get; } = new();
    public string SelectedFrequencyFieldId => $"psd.frequency-bin.{SelectedFrequencyIndex.ToString(CultureInfo.InvariantCulture)}";
    public TopomapField? TopomapField => Topomap?.Fields.FirstOrDefault(item => item.FieldId == SelectedFrequencyFieldId);

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
            UpdateFrequencies(dynamic);
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

    public void ApplyTopomap(TopomapResponse response) { Topomap = response; RaisePropertyChanged(nameof(TopomapField)); }
    public void ClearTopomap() { TopomapColorScales.Reset(); Topomap = null; }
    public void SetStatus(string value) => StatusText = value;

    private void UpdateFrequencies(DynamicWindowResponse response)
    {
        if (response.Result?.Axes is not { } axes || !axes.TryGetValue("frequency_hz", out var values) || values.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("PSD 结果缺少频率轴。");
        var options = values.EnumerateArray().Select((value, index) =>
        {
            if (value.ValueKind != System.Text.Json.JsonValueKind.Number || !value.TryGetDouble(out var hz) || !double.IsFinite(hz))
                throw new InvalidOperationException("PSD 频率轴包含无效值。");
            return new PsdFrequencyOption(index, hz);
        }).ToArray();
        if (options.Length == 0) throw new InvalidOperationException("PSD 频率轴为空。");
        FrequencyOptions = options;
        if (options.All(option => option.Index != SelectedFrequencyIndex)) SelectedFrequencyIndex = options[0].Index;
    }

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
        return start is double from && end is double to
            ? $"{from:0.###}–{to:0.###} s"
            : "实际窗口范围不可用";
    }
}

public sealed record PsdFrequencyOption(int Index, double FrequencyHz)
{
    public string Label => $"{FrequencyHz:0.###} Hz";
}
