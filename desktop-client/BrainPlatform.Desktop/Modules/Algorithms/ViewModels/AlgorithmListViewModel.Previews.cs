using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

public sealed partial class AlgorithmListViewModel
{
    private string psdFrequencyUnit = "Hz";
    private string psdValueUnit = "";
    private bool hasPsdPreview;
    private string psdFrequencyRangeText = "";
    private string psdValueRangeText = "";
    private StftPreview? stftPreview;
    private string rbpDeltaText = "不可用";
    private string rbpThetaText = "不可用";
    private string rbpAlphaText = "不可用";
    private string rbpBetaText = "不可用";

    public sealed record PsdPreviewPoint(double Frequency, double Value);

    public ObservableCollection<PsdPreviewPoint?> PsdPreviewPoints { get; } = [];
    public ObservableCollection<DynamicSeriesPoint> DynamicSeriesPoints { get; } = [];
    public ObservableCollection<DynamicWindowRow> DynamicWindowRows { get; } = [];
    public bool HasDynamicSeries => DynamicSeriesPoints.Count > 0;
    public bool HasDynamicWindows => DynamicWindowRows.Count > 0;
    public string PsdFrequencyUnit { get => psdFrequencyUnit; private set => SetProperty(ref psdFrequencyUnit, value); }
    public string PsdValueUnit { get => psdValueUnit; private set => SetProperty(ref psdValueUnit, value); }
    public bool HasPsdPreview { get => hasPsdPreview; private set => SetProperty(ref hasPsdPreview, value); }
    public string PsdFrequencyRangeText { get => psdFrequencyRangeText; private set => SetProperty(ref psdFrequencyRangeText, value); }
    public string PsdValueRangeText { get => psdValueRangeText; private set => SetProperty(ref psdValueRangeText, value); }
    public StftPreview? StftResult { get => stftPreview; private set => SetProperty(ref stftPreview, value); }
    public bool HasStftPreview => StftResult is not null;
    public string RbpDeltaText { get => rbpDeltaText; private set => SetProperty(ref rbpDeltaText, value); }
    public string RbpThetaText { get => rbpThetaText; private set => SetProperty(ref rbpThetaText, value); }
    public string RbpAlphaText { get => rbpAlphaText; private set => SetProperty(ref rbpAlphaText, value); }
    public string RbpBetaText { get => rbpBetaText; private set => SetProperty(ref rbpBetaText, value); }
    public string StftAxisText => StftResult is null ? "时间 (s)" :
        $"请求范围 {StftResult.RequestedRange.StartSeconds:0.###} - {StftResult.RequestedRange.EndSeconds:0.###} s  ·  时频中心 {StftResult.TimesSeconds[0]:0.###} - {StftResult.TimesSeconds[^1]:0.###} s  ·  频率 {StftResult.FrequenciesHz[0]:0.###} - {StftResult.FrequenciesHz[^1]:0.###} Hz  ·  {StftResult.PowerUnit}";

    private async Task LoadStructuredPreviewAsync(string runId, string algorithmId, bool dynamic = false)
    {
        try
        {
            // Dynamic PSD contains one row per analysis window. Keep the
            // preview bounded, but do not reject a normal 60-window result.
            var preview = await client.GetStructuredPreviewAsync(runId, algorithmId == "stft" ? 1_000_000 : 20_000, CancellationToken.None);
            StructuredPreviewText = AlgorithmResultFormatter.BuildStructuredPreview(preview);
            if (dynamic)
            {
                // Dynamic matrices are represented by bounded backend metadata
                // until the final chart redesign; no science is recomputed here.
                SetDynamicWindows(preview);
            }
            else if (algorithmId == "stft")
            {
                StftResult = StftPreview.Parse(preview);
                RaisePropertyChanged(nameof(HasStftPreview));
                RaisePropertyChanged(nameof(StftAxisText));
            }
            else BuildPsdPreview(preview);
        }
        catch (AlgorithmApiException exception) when (exception.Code == "REQUEST_INVALID" && algorithmId == "stft")
        {
            StructuredPreviewText = "STFT 结果已保存，但预览超过 100,000 个数值单元或无法读取；请缩短分析范围后重新运行。";
            ClearPreviews();
        }
        catch (AlgorithmApiException exception) when (exception.Code is "REQUEST_INVALID" or "RESOURCE_NOT_FOUND")
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Code}：{exception.Message}";
            ClearPreviews();
        }
        catch (Exception exception)
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Message}";
            ClearPreviews();
        }
    }

    private void ClearPreviews()
    {
        ClearPsdPreview();
        StftResult = null;
        DynamicSeriesText = "暂无动态结果。";
        DynamicSeriesPoints.Clear();
        DynamicWindowRows.Clear();
        RaisePropertyChanged(nameof(HasDynamicSeries));
        RaisePropertyChanged(nameof(HasDynamicWindows));
        RbpDeltaText = RbpThetaText = RbpAlphaText = RbpBetaText = "不可用";
        RaisePropertyChanged(nameof(HasStftPreview));
        RaisePropertyChanged(nameof(StftAxisText));
    }

    private void ClearPsdPreview()
    {
        PsdPreviewPoints.Clear();
        HasPsdPreview = false;
        PsdFrequencyUnit = "Hz";
        PsdValueUnit = "";
        PsdFrequencyRangeText = "";
        PsdValueRangeText = "";
    }

    private void SetDynamicSeries(AnalysisRunResponse run)
    {
        DynamicSeriesPoints.Clear();
        foreach (var point in DynamicResultPreview.ParseScalar(run))
            DynamicSeriesPoints.Add(point);
        RaisePropertyChanged(nameof(HasDynamicSeries));
    }

    private void SetDynamicWindows(StructuredPreviewResponse preview)
    {
        DynamicWindowRows.Clear();
        foreach (var window in DynamicResultPreview.ParseStructured(preview))
            DynamicWindowRows.Add(window);
        RaisePropertyChanged(nameof(HasDynamicWindows));
    }

    private void BuildPsdPreview(StructuredPreviewResponse preview)
    {
        ClearPsdPreview();
        if (!preview.Axes.TryGetValue("frequency_hz", out var frequencies) ||
            !preview.Arrays.TryGetValue("psd", out var values) ||
            frequencies.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array)
            return;

        PsdFrequencyUnit = AlgorithmResultFormatter.MetadataUnit(preview.AxisMetadata, "frequency_hz");
        PsdValueUnit = AlgorithmResultFormatter.MetadataUnit(preview.ArrayMetadata, "psd");
        var frequencyValues = frequencies.EnumerateArray().ToArray();
        var psdValues = values.EnumerateArray().ToArray();
        var count = Math.Min(frequencyValues.Length, psdValues.Length);
        for (var index = 0; index < count; index++)
        {
            if (frequencyValues[index].ValueKind != JsonValueKind.Number ||
                psdValues[index].ValueKind != JsonValueKind.Number ||
                !frequencyValues[index].TryGetDouble(out var frequency) ||
                !psdValues[index].TryGetDouble(out var value) ||
                !double.IsFinite(frequency) || !double.IsFinite(value))
            {
                PsdPreviewPoints.Add(null);
                continue;
            }
            PsdPreviewPoints.Add(new PsdPreviewPoint(frequency, value));
        }
        HasPsdPreview = PsdPreviewPoints.Any(point => point is not null);
        if (HasPsdPreview)
        {
            var available = PsdPreviewPoints.Where(point => point is not null).Select(point => point!).ToArray();
            PsdFrequencyRangeText = $"{available.Min(point => point.Frequency):0.###} - {available.Max(point => point.Frequency):0.###} {PsdFrequencyUnit}";
            PsdValueRangeText = $"{available.Min(point => point.Value):G3} - {available.Max(point => point.Value):G3} {PsdValueUnit}";
        }
    }

    private void UpdateRbpValues(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || !summary.TryGetProperty("metric", out var metric) ||
            !metric.TryGetProperty("band_values", out var values) || values.ValueKind != JsonValueKind.Object)
            return;
        RbpDeltaText = AlgorithmResultFormatter.FormatBandValue(values, "delta");
        RbpThetaText = AlgorithmResultFormatter.FormatBandValue(values, "theta");
        RbpAlphaText = AlgorithmResultFormatter.FormatBandValue(values, "alpha");
        RbpBetaText = AlgorithmResultFormatter.FormatBandValue(values, "beta");
    }

    private void UpdateDynamicRbpValues(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || !summary.TryGetProperty("metric", out var metric) ||
            !metric.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Array)
            return;
        var point = series.EnumerateArray()
            .LastOrDefault(item => item.TryGetProperty("band_values", out var bands) &&
                                   bands.ValueKind == JsonValueKind.Object &&
                                   bands.EnumerateObject().Any(entry => entry.Value.ValueKind == JsonValueKind.Number));
        if (point.ValueKind != JsonValueKind.Object || !point.TryGetProperty("band_values", out var values))
            return;
        RbpDeltaText = AlgorithmResultFormatter.FormatBandValue(values, "delta");
        RbpThetaText = AlgorithmResultFormatter.FormatBandValue(values, "theta");
        RbpAlphaText = AlgorithmResultFormatter.FormatBandValue(values, "alpha");
        RbpBetaText = AlgorithmResultFormatter.FormatBandValue(values, "beta");
    }
}
