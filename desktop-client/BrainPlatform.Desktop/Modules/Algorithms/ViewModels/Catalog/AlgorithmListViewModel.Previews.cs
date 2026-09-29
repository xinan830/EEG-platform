using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private string psdFrequencyUnit = "Hz";
    private string psdValueUnit = "";
    private bool hasPsdPreview;
    private string psdFrequencyRangeText = "";
    private string psdValueRangeText = "";
    private StftPreview? stftPreview;
    private StructuredPreviewResponse? psdStructuredPreview;
    private string rbpDeltaText = "不可用";
    private string rbpThetaText = "不可用";
    private string rbpAlphaText = "不可用";
    private string rbpBetaText = "不可用";
    private JsonElement[] dynamicPsdFrequencies = [];
    private JsonElement[] dynamicPsdRows = [];
    private bool dynamicPsdIsVoltsSquaredPerHz;

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
    public StructuredPreviewResponse? PsdStructuredPreview { get => psdStructuredPreview; private set => SetProperty(ref psdStructuredPreview, value); }
    public string PsdQualityText => PsdStructuredPreview?.Quality is { } quality
        ? AlgorithmResultFormatter.FormatQualityForDisplay(quality)
        : LastRun?.Error is { } error
            ? $"运行失败：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}"
            : "尚未运行分析";
    public string PsdWindowStateText => PsdStructuredPreview is null || PsdStructuredPreview.WindowStateCounts.Count == 0
        ? "暂无窗口状态"
        : string.Join("、", PsdStructuredPreview.WindowStateCounts.Select(item => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(item.Key)}：{item.Value}"));
    public bool HasStftPreview => StftResult is not null;
    public string RbpDeltaText { get => rbpDeltaText; private set => SetProperty(ref rbpDeltaText, value); }
    public string RbpThetaText { get => rbpThetaText; private set => SetProperty(ref rbpThetaText, value); }
    public string RbpAlphaText { get => rbpAlphaText; private set => SetProperty(ref rbpAlphaText, value); }
    public string RbpBetaText { get => rbpBetaText; private set => SetProperty(ref rbpBetaText, value); }
    public double DynamicTimeProgressPercent
    {
        get => DynamicPreviewDurationSeconds <= 0
            ? 0
            : Math.Clamp(DynamicPreviewCursorSeconds / DynamicPreviewDurationSeconds * 100.0, 0, 100);
        set
        {
            if (DynamicPreviewDurationSeconds <= 0 || DynamicWindowRows.Count == 0)
                return;
            var cursor = DynamicPreviewDurationSeconds * Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(cursor - DynamicPreviewCursorSeconds) < 1e-9)
                return;
            DynamicPreviewCursorSeconds = cursor;
            ReleaseDynamicPreviewRows();
            RaiseDynamicPreviewProperties();
        }
    }
    public string DynamicTimeProgressText => $"{DynamicPreviewCursorSeconds:0.###} / {DynamicPreviewDurationSeconds:0.###} s";
    public string DynamicCurrentWindowText => DynamicPreviewRows.LastOrDefault() is { } row
        ? $"当前窗口：{row.StartSeconds:0.###}–{row.EndSeconds:0.###} s（{row.State}）"
        : "当前窗口：尚未释放";
    private double DynamicPreviewDurationSeconds => new[]
    {
        RegisteredRecording?.DurationSeconds ?? 0,
        LastRun?.ActualRange?.EndSeconds ?? 0,
        DynamicWindowRows.Count > 0 ? DynamicWindowRows.Max(row => row.EndSeconds) : 0,
    }.Where(value => double.IsFinite(value) && value > 0).DefaultIfEmpty(0).Max();
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
                SetDynamicWindows(preview, algorithmId);
                if (algorithmId == "psd")
                {
                    PsdStructuredPreview = preview;
                    SetPsdMetadata(preview);
                    // Show the first complete dynamic window immediately. The
                    // timeline controls can then replace it as the cursor moves.
                    var firstCompleteIndex = DynamicWindowRows
                        .Select((row, index) => (row, index))
                        .Where(item => string.Equals(item.row.State, "完整", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(item.row.State, "Complete", StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.index)
                        .FirstOrDefault(-1);
                    if (firstCompleteIndex >= 0 && firstCompleteIndex < dynamicPsdRows.Length)
                        BuildDynamicPsdPreview(firstCompleteIndex);
                    else
                        ClearPsdPreview();
                    RaisePropertyChanged(nameof(PsdQualityText));
                    RaisePropertyChanged(nameof(PsdWindowStateText));
                }
            }
            else if (algorithmId == "stft")
            {
                StftResult = StftPreview.Parse(preview);
                RaisePropertyChanged(nameof(HasStftPreview));
                RaisePropertyChanged(nameof(StftAxisText));
            }
            else
            {
                PsdStructuredPreview = preview;
                BuildPsdPreview(preview);
                RaisePropertyChanged(nameof(PsdQualityText));
                RaisePropertyChanged(nameof(PsdWindowStateText));
            }
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
        StopDynamicPreviewTimer();
        DynamicPreviewCursorSeconds = 0;
        DynamicPreviewRows.Clear();
        dynamicPsdFrequencies = [];
        dynamicPsdRows = [];
        RaiseDynamicPreviewProperties();
        // A preview belongs to the exact algorithm/mode/configuration that
        // produced its Run. Do not leave a previous dynamic Run visible after
        // switching to static mode (or changing the recording/algorithm).
        LastRun = null;
        RaisePropertyChanged(nameof(IsRunActive));
        ClearPsdPreview();
        StftResult = null;
        PsdStructuredPreview = null;
        DynamicSeriesText = "暂无动态结果。";
        DynamicSeriesPoints.Clear();
        DynamicWindowRows.Clear();
        RaisePropertyChanged(nameof(HasDynamicSeries));
        RaisePropertyChanged(nameof(HasDynamicWindows));
        RbpDeltaText = RbpThetaText = RbpAlphaText = RbpBetaText = "不可用";
        RaisePropertyChanged(nameof(HasStftPreview));
        RaisePropertyChanged(nameof(StftAxisText));
        RaisePropertyChanged(nameof(PsdQualityText));
        RaisePropertyChanged(nameof(PsdWindowStateText));
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

    private void SetDynamicWindows(StructuredPreviewResponse preview, string algorithmId)
    {
        DynamicWindowRows.Clear();
        foreach (var window in DynamicResultPreview.ParseStructured(preview))
            DynamicWindowRows.Add(window);
        if (algorithmId == "psd")
            SetDynamicPsdMatrix(preview);
        ResetDynamicPreview();
        if (algorithmId == "psd")
        {
            // The result is already complete when the preview is loaded. Start
            // at the first complete window so the chart is not blank at 0 s
            // while still preserving the timeline for subsequent playback.
            var firstComplete = DynamicWindowRows.FirstOrDefault(row => IsCompleteWindowState(row.State));
            DynamicPreviewCursorSeconds = firstComplete?.EndSeconds ?? 0;
            ReleaseDynamicPreviewRows();
        }
        RaisePropertyChanged(nameof(HasDynamicWindows));
    }

    private void SetDynamicPsdMatrix(StructuredPreviewResponse preview)
    {
        dynamicPsdFrequencies = preview.Axes.TryGetValue("frequency_hz", out var frequencies) && frequencies.ValueKind == JsonValueKind.Array
            ? frequencies.EnumerateArray().ToArray()
            : [];
        dynamicPsdRows = preview.Arrays.TryGetValue("psd", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().ToArray()
            : [];
        dynamicPsdIsVoltsSquaredPerHz = IsVoltsSquaredPerHz(preview);
    }

    private void StartDynamicPreview()
    {
        if (!CanStartDynamicPreview()) return;
        IsDynamicPreviewPlaying = true;
        dynamicPreviewTimer.Start();
        RaiseDynamicPreviewProperties();
    }

    private void PauseDynamicPreview()
    {
        StopDynamicPreviewTimer();
        RaiseDynamicPreviewProperties();
    }

    private void StepDynamicPreview()
    {
        if (!CanStepDynamicPreview()) return;
        DynamicPreviewCursorSeconds = Math.Min(
            DynamicWindowRows.Max(row => row.EndSeconds),
            DynamicPreviewCursorSeconds + ParsePreviewStep());
        ReleaseDynamicPreviewRows();
        RaiseDynamicPreviewProperties();
    }

    private void ResetDynamicPreview()
    {
        StopDynamicPreviewTimer();
        DynamicPreviewCursorSeconds = 0;
        DynamicPreviewRows.Clear();
        ClearPsdPreview();
        RaisePropertyChanged(nameof(PsdPreviewPoints));
        RaiseDynamicPreviewProperties();
    }

    private void AdvanceDynamicPreview() => StepDynamicPreview();

    internal void DynamicPreviewCursorSecondsForTest(double seconds)
    {
        SeekDynamicPreviewSeconds(seconds);
    }

    private void SeekDynamicPreviewSeconds(double seconds)
    {
        if (DynamicWindowRows.Count == 0)
            return;
        var maximum = DynamicWindowRows.Max(row => row.EndSeconds);
        DynamicPreviewCursorSeconds = Math.Clamp(seconds, 0, maximum);
        ReleaseDynamicPreviewRows();
        RaiseDynamicPreviewProperties();
    }

    private bool CanStartDynamicPreview() => IsDynamicMode && HasDynamicPreview && !IsDynamicPreviewPlaying && DynamicPreviewCursorSeconds < DynamicWindowRows.Max(row => row.EndSeconds);
    private bool CanStepDynamicPreview() => IsDynamicMode && HasDynamicPreview && DynamicPreviewCursorSeconds < DynamicWindowRows.Max(row => row.EndSeconds);
    private bool CanResetDynamicPreview() => HasDynamicPreview && (DynamicPreviewCursorSeconds > 0 || DynamicPreviewRows.Count > 0 || IsDynamicPreviewPlaying);

    private double ParsePreviewStep() => double.TryParse(DynamicStepText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var step) && step > 0 ? step : 1.0;

    private void ReleaseDynamicPreviewRows()
    {
        DynamicPreviewRows.Clear();
        foreach (var row in DynamicWindowRows.Where(row => row.EndSeconds <= DynamicPreviewCursorSeconds + 1e-9))
            DynamicPreviewRows.Add(row);
        var latestIndex = DynamicWindowRows.Count == 0 || DynamicPreviewRows.Count == 0
            ? -1
            : DynamicWindowRows.IndexOf(DynamicPreviewRows[^1]);
        if (latestIndex >= 0 && latestIndex < dynamicPsdRows.Length && IsCompleteWindowState(DynamicPreviewRows[^1].State))
            BuildDynamicPsdPreview(latestIndex);
        else
            ClearPsdPreview();
        if (DynamicPreviewCursorSeconds >= DynamicWindowRows.Max(row => row.EndSeconds))
            StopDynamicPreviewTimer();
    }

    private void StopDynamicPreviewTimer()
    {
        dynamicPreviewTimer.Stop();
        IsDynamicPreviewPlaying = false;
    }

    private void RaiseDynamicPreviewProperties()
    {
        RaisePropertyChanged(nameof(DynamicPreviewCursorText));
        RaisePropertyChanged(nameof(HasDynamicPreview));
        RaisePropertyChanged(nameof(DynamicPreviewStatusText));
        RaisePropertyChanged(nameof(DynamicPreviewRows));
        RaisePropertyChanged(nameof(DynamicTimeProgressPercent));
        RaisePropertyChanged(nameof(DynamicTimeProgressText));
        RaisePropertyChanged(nameof(DynamicCurrentWindowText));
        RaisePropertyChanged(nameof(PsdPreviewPoints));
        (StartDynamicPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StepDynamicPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetDynamicPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void BuildPsdPreview(StructuredPreviewResponse preview)
    {
        ClearPsdPreview();
        if (!preview.Axes.TryGetValue("frequency_hz", out var frequencies) ||
            !preview.Arrays.TryGetValue("psd", out var values) ||
            frequencies.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array)
            return;

        SetPsdMetadata(preview);
        var frequencyValues = frequencies.EnumerateArray().ToArray();
        var psdValues = values.EnumerateArray().ToArray();
        // Dynamic PSD is window x frequency. The chart is a 2D projection, so
        // display the latest complete finite row returned by the backend.
        if (psdValues.Length > 0 && psdValues[0].ValueKind == JsonValueKind.Array)
        {
            var latestRow = psdValues.Reverse().FirstOrDefault(row => row.ValueKind == JsonValueKind.Array &&
                row.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number)));
            psdValues = latestRow.ValueKind == JsonValueKind.Array ? latestRow.EnumerateArray().ToArray() : [];
        }
        SetPsdPoints(frequencyValues, psdValues, IsVoltsSquaredPerHz(preview));
    }

    private void SetPsdMetadata(StructuredPreviewResponse preview)
    {
        PsdFrequencyUnit = AlgorithmResultFormatter.MetadataUnit(preview.AxisMetadata, "frequency_hz");
        PsdValueUnit = AlgorithmResultFormatter.MetadataUnit(preview.ArrayMetadata, "psd");
    }

    private void BuildDynamicPsdPreview(int rowIndex)
    {
        if (dynamicPsdRows[rowIndex].ValueKind != JsonValueKind.Array)
        {
            ClearPsdPreview();
            return;
        }
        SetPsdPoints(dynamicPsdFrequencies, dynamicPsdRows[rowIndex].EnumerateArray().ToArray(),
            dynamicPsdIsVoltsSquaredPerHz);
    }

    private static bool IsVoltsSquaredPerHz(StructuredPreviewResponse preview) =>
        preview.ArrayMetadata.TryGetValue("psd", out var metadata) &&
        metadata.ValueKind == JsonValueKind.Object &&
        metadata.TryGetProperty("unit", out var unit) &&
        string.Equals(unit.GetString(), "V^2/Hz", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompleteWindowState(string state) =>
        string.Equals(state, "Complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "完整", StringComparison.OrdinalIgnoreCase);

    private void SetPsdPoints(IReadOnlyList<JsonElement> frequencyValues, IReadOnlyList<JsonElement> psdValues, bool convertFromVolts)
    {
        PsdPreviewPoints.Clear();
        var count = Math.Min(frequencyValues.Count, psdValues.Count);
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
            // Convert only when the backend explicitly declares V²/Hz. Some
            // result services already expose uV²/Hz values.
            PsdPreviewPoints.Add(new PsdPreviewPoint(frequency, convertFromVolts ? value * 1_000_000_000_000d : value));
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
