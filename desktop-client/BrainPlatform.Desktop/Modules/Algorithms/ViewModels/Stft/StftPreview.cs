using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

public sealed class StftPreview
{
    private StftPreview(double[] times, double[] frequencies, double?[][] powerDb, double minimumDb, double maximumDb, TimeRange requestedRange)
    {
        TimesSeconds = times;
        FrequenciesHz = frequencies;
        PowerDb = powerDb;
        MinimumDb = minimumDb;
        MaximumDb = maximumDb;
        RequestedRange = requestedRange;
    }

    public double[] TimesSeconds { get; }
    public double[] FrequenciesHz { get; }
    public double?[][] PowerDb { get; }
    public double MinimumDb { get; }
    public double MaximumDb { get; }
    public TimeRange RequestedRange { get; }
    public string PowerUnit => "dB re 1 uV^2/Hz";

    public static StftPreview Append(StftPreview existing, StftPreview incoming)
    {
        if (existing.FrequenciesHz.Length != incoming.FrequenciesHz.Length ||
            existing.FrequenciesHz.Zip(incoming.FrequenciesHz)
                .Any(pair => Math.Abs(pair.First - pair.Second) > 1e-9))
            throw new InvalidOperationException("STFT 追加窗口的频率轴与历史不一致。");

        var columns = existing.TimesSeconds
            .Select((time, index) => (time, row: existing.PowerDb[index]))
            .Concat(incoming.TimesSeconds.Select((time, index) => (time, row: incoming.PowerDb[index])))
            .GroupBy(item => Math.Round(item.time, 9))
            .Select(group => group.First())
            .OrderBy(item => item.time)
            .ToArray();
        return BuildPreview(columns.Select(item => item.time).ToArray(), existing.FrequenciesHz,
            columns.Select(item => item.row).ToArray(),
            new TimeRange(
                Math.Min(existing.RequestedRange.StartSeconds, incoming.RequestedRange.StartSeconds),
                Math.Max(existing.RequestedRange.EndSeconds, incoming.RequestedRange.EndSeconds)));
    }

    public static StftPreview Parse(StructuredPreviewResponse preview) => ParseCore(preview, null);

    public static StftPreview ParseDynamic(StructuredPreviewResponse preview, int windowIndex) =>
        ParseCore(preview, windowIndex);

    public static StftPreview FromWindow(DynamicWindowResponse response)
    {
        if (response.State != "ready" || response.Result is not { } result ||
            result.State != "ready" || result.OutputKind != "time_frequency" || result.Quality != "clean" || response.Quality != "clean" ||
            result.Axes is null || result.AxisUnits is null || result.Arrays is null || result.ArrayUnits is null ||
            response.Range is not { } range)
            throw new InvalidOperationException("STFT 按需窗口不是可显示的时频结果。");
        if (result.Axes.Count != 2 || result.Arrays.Count != 2 || result.AxisUnits.Count != 2 || result.ArrayUnits.Count != 2 ||
            result.AxisUnits.GetValueOrDefault("time_center_s") != "s" ||
            result.AxisUnits.GetValueOrDefault("frequency_hz") != "Hz" ||
            result.ArrayUnits.GetValueOrDefault("power_linear") != "V^2/Hz" ||
            result.ArrayUnits.GetValueOrDefault("power_db") != "dB re 1 uV^2/Hz")
            throw new InvalidOperationException("STFT 按需窗口的轴或功率单位不符合展示契约。");
        if (!result.Axes.TryGetValue("time_center_s", out var timesAxis) ||
            !result.Axes.TryGetValue("frequency_hz", out var frequencyAxis) ||
            !result.Arrays.TryGetValue("power_linear", out var linearMatrix) ||
            !result.Arrays.TryGetValue("power_db", out var matrix) ||
            linearMatrix.ValueKind != JsonValueKind.Array || matrix.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("STFT 按需窗口缺少时频轴或功率矩阵。");
        if (result.Provenance is not { ValueKind: JsonValueKind.Object } provenance ||
            Text(provenance, "preprocessing_scope") != "whole_recording_before_window_slice" ||
            Text(provenance, "inner_time_axis") != "relative_window_time_center_s" ||
            Text(provenance, "analysis_state") is not ("Partial" or "Complete"))
            throw new InvalidOperationException("STFT 按需窗口缺少连续预处理或时间轴 provenance。");
        var times = ReadAxis(new Dictionary<string, JsonElement> { ["time_center_s"] = timesAxis }, "time_center_s");
        var frequencies = ReadAxis(new Dictionary<string, JsonElement> { ["frequency_hz"] = frequencyAxis }, "frequency_hz");
        if (!double.IsFinite(range.StartSeconds) || !double.IsFinite(range.EndSeconds) ||
            range.StartSeconds < 0 || range.EndSeconds <= range.StartSeconds)
            throw new InvalidOperationException("STFT 按需窗口范围无效。");
        if (matrix.GetArrayLength() != times.Length)
            throw new InvalidOperationException("STFT 按需窗口矩阵的时间维度不一致。");
        ValidateMatrix(linearMatrix, times.Length, frequencies.Length, "STFT 线性功率矩阵");
        var windowDuration = range.EndSeconds - range.StartSeconds;
        if (times.Any(time => time < 0 || time > windowDuration))
            throw new InvalidOperationException("STFT 按需窗口的内层时间轴超出窗口范围。");
        var absoluteTimes = times.Select(time => time + range.StartSeconds).ToArray();
        return BuildPreview(absoluteTimes, frequencies, matrix, new TimeRange(range.StartSeconds, range.EndSeconds));
    }

    private static StftPreview ParseCore(StructuredPreviewResponse preview, int? windowIndex)
    {
        if (preview.Output is not { } output || output.ValueKind != JsonValueKind.Object ||
            !output.TryGetProperty("kind", out var kind) || kind.GetString() != "time_frequency" ||
            preview.ChannelOrder.Count != 1)
            throw new InvalidOperationException("STFT 预览不是单通道时频结果。");

        if (windowIndex is null && preview.Windows.Count != 0)
            throw new InvalidOperationException("STFT 静态预览包含动态窗口。");
        if (windowIndex is int index && (index < 0 || index >= preview.Windows.Count))
            throw new InvalidOperationException("STFT 动态窗口索引超出范围。");

        RequireUnit(preview.AxisMetadata, "time_center_s", "s");
        RequireUnit(preview.AxisMetadata, "frequency_hz", "Hz");
        RequireUnit(preview.ArrayMetadata, "power_db", "dB re 1 uV^2/Hz");
        var times = ReadAxis(preview.Axes, "time_center_s");
        var frequencies = ReadAxis(preview.Axes, "frequency_hz");
        if (!preview.Arrays.TryGetValue("power_db", out var matrix) || matrix.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("STFT 缺少功率矩阵。");
        TimeRange requestedRange;
        if (windowIndex is int selectedIndex)
        {
            if (matrix.GetArrayLength() != preview.Windows.Count ||
                matrix[selectedIndex].ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("STFT 动态矩阵的窗口维度不一致。");
            var window = preview.Windows[selectedIndex];
            if (window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("state", out var state) || state.GetString() != "Complete" ||
                !window.TryGetProperty("start_s", out var start) || !start.TryGetDouble(out var startSeconds) ||
                !window.TryGetProperty("end_s", out var end) || !end.TryGetDouble(out var endSeconds))
                throw new InvalidOperationException("STFT 当前动态窗口没有完整结果。");
            matrix = matrix[selectedIndex];
            times = times.Select(time => time + startSeconds).ToArray();
            requestedRange = new TimeRange(startSeconds, endSeconds);
        }
        else
            requestedRange = preview.RequestedRange ?? throw new InvalidOperationException("STFT 缺少请求分析范围。");
        if (
            matrix.GetArrayLength() != times.Length)
            throw new InvalidOperationException("STFT 功率矩阵的时间维度与后端时间轴不一致。");

        return BuildPreview(times, frequencies, matrix, requestedRange);
    }

    private static StftPreview BuildPreview(double[] times, double[] frequencies, JsonElement matrix, TimeRange requestedRange)
    {
        var rows = new double?[times.Length][];
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var timeIndex = 0;
        foreach (var row in matrix.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != frequencies.Length)
                throw new InvalidOperationException("STFT 功率矩阵的频率维度与后端频率轴不一致。");
            rows[timeIndex] = new double?[frequencies.Length];
            var frequencyIndex = 0;
            foreach (var cell in row.EnumerateArray())
            {
                if (cell.ValueKind == JsonValueKind.Number && cell.TryGetDouble(out var value) && double.IsFinite(value))
                {
                    rows[timeIndex][frequencyIndex] = value;
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
                else if (cell.ValueKind != JsonValueKind.Null)
                    throw new InvalidOperationException("STFT 功率矩阵包含非数值单元。");
                frequencyIndex++;
            }
            timeIndex++;
        }
        if (!double.IsFinite(minimum))
            throw new InvalidOperationException("STFT 范围内没有可显示的有效功率单元。");
        return new StftPreview(times, frequencies, rows, minimum, maximum, requestedRange);
    }

    private static StftPreview BuildPreview(double[] times, double[] frequencies, double?[][] rows, TimeRange requestedRange)
    {
        var finite = rows.SelectMany(row => row).Where(value => value is double number && double.IsFinite(number)).Select(value => value!.Value).ToArray();
        if (finite.Length == 0) throw new InvalidOperationException("STFT 历史没有可显示的有效功率单元。");
        return new StftPreview(times, frequencies, rows, finite.Min(), finite.Max(), requestedRange);
    }

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() ?? "" : "";

    private static void ValidateMatrix(JsonElement matrix, int timeCount, int frequencyCount, string label)
    {
        if (matrix.GetArrayLength() != timeCount)
            throw new InvalidOperationException($"{label}的时间维度不一致。");
        foreach (var row in matrix.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != frequencyCount ||
                row.EnumerateArray().Any(cell => cell.ValueKind != JsonValueKind.Number ||
                    !cell.TryGetDouble(out var value) || !double.IsFinite(value)))
                throw new InvalidOperationException($"{label}包含无效数值或 shape。");
        }
    }

    private static double[] ReadAxis(IReadOnlyDictionary<string, JsonElement> axes, string key)
    {
        if (!axes.TryGetValue(key, out var axis) || axis.ValueKind != JsonValueKind.Array || axis.GetArrayLength() == 0)
            throw new InvalidOperationException($"STFT 缺少 {key} 轴。");
        var values = axis.EnumerateArray().Select(value =>
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
                ? number : throw new InvalidOperationException($"STFT {key} 轴包含无效坐标。")).ToArray();
        if (values.Zip(values.Skip(1)).Any(pair => pair.First >= pair.Second))
            throw new InvalidOperationException($"STFT {key} 轴未严格递增。");
        if (values.Length > 2)
        {
            var step = values[1] - values[0];
            if (values.Zip(values.Skip(1)).Any(pair => Math.Abs((pair.Second - pair.First) - step) > Math.Max(1e-6, step * 1e-5)))
                throw new InvalidOperationException($"STFT {key} 轴不等间距，无法按等宽格子展示。");
        }
        return values;
    }

    private static void RequireUnit(IReadOnlyDictionary<string, JsonElement> metadata, string key, string unit)
    {
        if (!metadata.TryGetValue(key, out var item) || item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("unit", out var declared) || declared.GetString() != unit)
            throw new InvalidOperationException($"STFT {key} 单位与显示契约不符。");
    }
}
