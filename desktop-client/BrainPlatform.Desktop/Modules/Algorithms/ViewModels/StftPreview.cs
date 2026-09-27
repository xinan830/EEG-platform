using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

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

    public static StftPreview Parse(StructuredPreviewResponse preview)
    {
        if (preview.Output is not { } output || output.ValueKind != JsonValueKind.Object ||
            !output.TryGetProperty("kind", out var kind) || kind.GetString() != "time_frequency" ||
            preview.Windows.Count != 0 || preview.ChannelOrder.Count != 1)
            throw new InvalidOperationException("STFT 预览不是单通道静态时频结果。");

        RequireUnit(preview.AxisMetadata, "time_center_s", "s");
        RequireUnit(preview.AxisMetadata, "frequency_hz", "Hz");
        RequireUnit(preview.ArrayMetadata, "power_db", "dB re 1 uV^2/Hz");
        var times = ReadAxis(preview.Axes, "time_center_s");
        var frequencies = ReadAxis(preview.Axes, "frequency_hz");
        if (!preview.Arrays.TryGetValue("power_db", out var matrix) || matrix.ValueKind != JsonValueKind.Array ||
            matrix.GetArrayLength() != times.Length)
            throw new InvalidOperationException("STFT 功率矩阵的时间维度与后端时间轴不一致。");

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
        var requestedRange = preview.RequestedRange ?? throw new InvalidOperationException("STFT 缺少请求分析范围。");
        return new StftPreview(times, frequencies, rows, minimum, maximum, requestedRange);
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
