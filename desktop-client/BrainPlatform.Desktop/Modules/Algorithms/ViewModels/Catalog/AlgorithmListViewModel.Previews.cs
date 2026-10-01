using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private readonly PsdPreviewState psdPreview = new();

    public sealed record PsdPreviewPoint(double Frequency, double Value);
    public sealed record PsdBandSharePoint(string Name, string Range, double Share, string ShareText, string Color, bool IsAvailable = true);

    internal PsdPreviewState PsdPreviewState => psdPreview;
    public ObservableCollection<PsdPreviewPoint?> PsdPreviewPoints => psdPreview.Points;
    public ObservableCollection<PsdBandSharePoint> PsdBandSharePoints => psdPreview.BandShares;
    public ObservableCollection<DynamicSeriesPoint> DynamicSeriesPoints { get; } = [];
    public ObservableCollection<DynamicWindowRow> DynamicWindowRows { get; } = [];
    public bool HasDynamicSeries => DynamicSeriesPoints.Count > 0;
    public bool HasDynamicWindows => DynamicWindowRows.Count > 0;
    public string PsdFrequencyUnit { get => psdPreview.FrequencyUnit; private set => psdPreview.FrequencyUnit = value; }
    public string PsdValueUnit { get => psdPreview.ValueUnit; private set => psdPreview.ValueUnit = value; }
    public bool HasPsdPreview { get => psdPreview.HasPreview; private set => psdPreview.HasPreview = value; }
    public string PsdFrequencyRangeText { get => psdPreview.FrequencyRangeText; private set => psdPreview.FrequencyRangeText = value; }
    public string PsdValueRangeText { get => psdPreview.ValueRangeText; private set => psdPreview.ValueRangeText = value; }
    public StructuredPreviewResponse? PsdStructuredPreview { get => psdPreview.StructuredPreview; private set => psdPreview.StructuredPreview = value; }
    public string PsdQualityText => PsdStructuredPreview?.Quality is { } quality
        ? AlgorithmResultFormatter.FormatQualityForDisplay(quality)
        : LastRun?.Error is { } error
            ? $"运行失败：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}"
            : "尚未运行分析";
    public string PsdWindowStateText => PsdStructuredPreview is null || PsdStructuredPreview.WindowStateCounts.Count == 0
        ? "暂无窗口状态"
        : string.Join("、", PsdStructuredPreview.WindowStateCounts.Select(item => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(item.Key)}：{item.Value}"));
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
    internal async Task LoadStructuredPreviewAsync(string runId, bool dynamic = false)
    {
        try
        {
            // Dynamic PSD contains one row per analysis window. Keep the
            // preview bounded, but do not reject a normal 60-window result.
            var preview = await client.GetStructuredPreviewAsync(runId, 20_000, CancellationToken.None);
            StructuredPreviewText = AlgorithmResultFormatter.BuildStructuredPreview(preview);
            if (dynamic)
            {
                // Dynamic matrices are displayed one backend window at a time;
                // no scientific values are recomputed here.
                // The structured preview must be assigned first because
                // SetDynamicWindows immediately releases the first complete
                // window and builds both PSD and band-share views.
                PsdStructuredPreview = preview;
                SetDynamicWindows(preview, StructuredPreviewKind.Psd);
                SetPsdMetadata(preview);
                // Show the first complete dynamic window immediately. The
                // timeline controls can then replace it as the cursor moves.
                var firstCompleteIndex = DynamicWindowRows
                    .Select((row, index) => (row, index))
                    .Where(item => string.Equals(item.row.State, "完整", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.row.State, "Complete", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.index)
                    .FirstOrDefault(-1);
                if (firstCompleteIndex >= 0 && firstCompleteIndex < psdPreview.DynamicRows.Length)
                    BuildDynamicPsdPreview(firstCompleteIndex);
                else
                    ClearPsdPreview();
                RaisePropertyChanged(nameof(PsdQualityText));
                RaisePropertyChanged(nameof(PsdWindowStateText));
            }
            else
            {
                PsdStructuredPreview = preview;
                BuildPsdPreview(preview);
                RaisePropertyChanged(nameof(PsdQualityText));
                RaisePropertyChanged(nameof(PsdWindowStateText));
            }
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
        psdPreview.DynamicFrequencies = [];
        psdPreview.DynamicRows = [];
        RaiseDynamicPreviewProperties();
        // A preview belongs to the exact algorithm/mode/configuration that
        // produced its Run. Do not leave a previous dynamic Run visible after
        // switching to static mode (or changing the recording/algorithm).
        LastRun = null;
        RaisePropertyChanged(nameof(IsRunActive));
        ClearPsdPreview();
        ClearStftPreview();
        PsdStructuredPreview = null;
        DynamicSeriesText = "暂无动态结果。";
        DynamicSeriesPoints.Clear();
        DynamicWindowRows.Clear();
        RaisePropertyChanged(nameof(HasDynamicSeries));
        RaisePropertyChanged(nameof(HasDynamicWindows));
        RaisePropertyChanged(nameof(PsdQualityText));
        RaisePropertyChanged(nameof(PsdWindowStateText));
    }

    private void ClearPsdPreview()
    {
        PsdPreviewPoints.Clear();
        PsdBandSharePoints.Clear();
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

    internal void SetDynamicWindows(StructuredPreviewResponse preview, StructuredPreviewKind kind)
    {
        DynamicWindowRows.Clear();
        foreach (var window in DynamicResultPreview.ParseStructured(preview))
            DynamicWindowRows.Add(window);
        if (kind == StructuredPreviewKind.Psd)
            SetDynamicPsdMatrix(preview);
        ResetDynamicPreview();
        if (kind is StructuredPreviewKind.Psd or StructuredPreviewKind.Stft)
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
        psdPreview.DynamicFrequencies = preview.Axes.TryGetValue("frequency_hz", out var frequencies) && frequencies.ValueKind == JsonValueKind.Array
            ? frequencies.EnumerateArray().ToArray()
            : [];
        psdPreview.DynamicRows = preview.Arrays.TryGetValue("psd", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().ToArray()
            : [];
        psdPreview.DynamicIsVoltsSquaredPerHz = IsVoltsSquaredPerHz(preview);
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
        SetStftResult(null);
        RaisePropertyChanged(nameof(PsdPreviewPoints));
        RaiseDynamicPreviewProperties();
    }

    private void AdvanceDynamicPreview() => StepDynamicPreview();

    internal void DynamicPreviewCursorSecondsForTest(double seconds)
    {
        SeekDynamicPreviewSeconds(seconds);
    }

    internal void SeekDynamicPreviewSeconds(double seconds)
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
        if (latestIndex >= 0 && latestIndex < psdPreview.DynamicRows.Length && IsCompleteWindowState(DynamicPreviewRows[^1].State))
            BuildDynamicPsdPreview(latestIndex);
        else
            ClearPsdPreview();
        UpdateDynamicStftPreview(latestIndex,
            latestIndex >= 0 && IsCompleteWindowState(DynamicPreviewRows[^1].State));
        if (DynamicWindowRows.Count > 0 && DynamicPreviewCursorSeconds >= DynamicWindowRows.Max(row => row.EndSeconds))
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
        SetPsdBandShares(preview, -1);
    }

    private void SetPsdMetadata(StructuredPreviewResponse preview)
    {
        PsdFrequencyUnit = AlgorithmResultFormatter.MetadataUnit(preview.AxisMetadata, "frequency_hz");
        PsdValueUnit = AlgorithmResultFormatter.MetadataUnit(preview.ArrayMetadata, "psd");
    }

    private void BuildDynamicPsdPreview(int rowIndex)
    {
        if (psdPreview.DynamicRows[rowIndex].ValueKind != JsonValueKind.Array)
        {
            ClearPsdPreview();
            return;
        }
        SetPsdPoints(psdPreview.DynamicFrequencies, psdPreview.DynamicRows[rowIndex].EnumerateArray().ToArray(),
            psdPreview.DynamicIsVoltsSquaredPerHz);
        SetPsdBandShares(PsdStructuredPreview, rowIndex);
    }

    private void SetPsdBandShares(StructuredPreviewResponse? preview, int rowIndex)
    {
        PsdBandSharePoints.Clear();
        if (preview is null)
            return;
        if (!preview.Arrays.TryGetValue("band_share", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            SetLegacyPsdBandShares(preview);
            return;
        }
        var row = values;
        if (values.GetArrayLength() > 0 && values[0].ValueKind == JsonValueKind.Array)
        {
            var index = rowIndex >= 0 ? rowIndex : values.GetArrayLength() - 1;
            if (index < 0 || index >= values.GetArrayLength())
                return;
            row = values[index];
        }
        var labels = new[] { ("Delta", "1–4 Hz", "#4F8EDC"), ("Theta", "4–8 Hz", "#56B88A"), ("Alpha", "8–13 Hz", "#E77B88"), ("Beta", "13–30 Hz", "#D99A38"), ("Gamma", "30–50 Hz", "#8D73D1") };
        for (var index = 0; index < Math.Min(5, row.GetArrayLength()); index++)
        {
            var item = row[index];
            var label = labels[index];
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var share) || !double.IsFinite(share))
            {
                PsdBandSharePoints.Add(new PsdBandSharePoint(label.Item1, label.Item2, 0, "不可用", label.Item3, false));
                continue;
            }
            share = Math.Clamp(share, 0, 1);
            PsdBandSharePoints.Add(new PsdBandSharePoint(label.Item1, label.Item2, share, $"{share:P1}", label.Item3));
        }
        RaisePropertyChanged(nameof(PsdBandSharePoints));
    }

    private void SetLegacyPsdBandShares(StructuredPreviewResponse preview)
    {
        if (preview.SpectralEvidence is not { } evidence || evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("relative_band_power", out var relative) || relative.ValueKind != JsonValueKind.Object)
            return;
        var channel = preview.ChannelOrder.FirstOrDefault();
        var values = !string.IsNullOrWhiteSpace(channel) && relative.TryGetProperty(channel, out var channelValues)
            ? channelValues
            : relative;
        var labels = new[] { ("Delta", "1–4 Hz", "#4F8EDC", "delta"), ("Theta", "4–8 Hz", "#56B88A", "theta"), ("Alpha", "8–13 Hz", "#E77B88", "alpha"), ("Beta", "13–30 Hz", "#D99A38", "beta") };
        foreach (var label in labels)
        {
            if (!values.TryGetProperty(label.Item4, out var item) || item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var share) || !double.IsFinite(share))
            {
                PsdBandSharePoints.Add(new PsdBandSharePoint(label.Item1, label.Item2, 0, "不可用", label.Item3, false));
                continue;
            }
            PsdBandSharePoints.Add(new PsdBandSharePoint(label.Item1, label.Item2, Math.Clamp(share, 0, 1), $"{share:P1}", label.Item3));
        }
        RaisePropertyChanged(nameof(PsdBandSharePoints));
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

}
