using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed record ReviewPsdProjection(double[] Frequencies, double[] Values)
{
    public static ReviewPsdProjection From(WindowPreviewResponse response)
    {
        if (response.Axes is null || !response.Axes.TryGetValue("axis_frequency_hz", out var axis) ||
            response.Arrays is null || !response.Arrays.TryGetValue("psd", out var psd))
            throw new InvalidOperationException("PSD 窗口缺少频率轴或谱数组。");
        if (response.ArrayMetadata is null || !response.ArrayMetadata.TryGetValue("psd", out var metadata) ||
            !metadata.TryGetProperty("unit", out var unit) || unit.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("PSD 窗口缺少单位声明。");
        var factor = unit.GetString() switch
        {
            "V^2/Hz" => 1e12,
            "uV^2/Hz" => 1d,
            _ => throw new InvalidOperationException("PSD 窗口单位不受支持。"),
        };
        var frequencies = Numbers(axis);
        var values = Numbers(psd);
        if (frequencies.Length < 2 || frequencies.Length != values.Length)
            throw new InvalidOperationException("PSD 数组长度与频率轴不一致。");
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] < 0 || !double.IsFinite(frequencies[index]) ||
                (index > 0 && frequencies[index] <= frequencies[index - 1]))
                throw new InvalidOperationException("PSD 数值或频率顺序无效。");
            values[index] *= factor;
            if (!double.IsFinite(values[index])) throw new InvalidOperationException("PSD 单位转换超出范围。");
        }
        return new(frequencies, values);
    }

    private static double[] Numbers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("PSD 数组格式无效。");
        return element.EnumerateArray().Select(item =>
            item.TryGetDouble(out var value) && double.IsFinite(value) ? value :
            throw new InvalidOperationException("PSD 窗口包含不可用数值。")).ToArray();
    }
}
