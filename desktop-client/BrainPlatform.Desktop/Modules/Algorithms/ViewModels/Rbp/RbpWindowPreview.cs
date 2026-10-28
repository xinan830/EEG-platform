using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed partial class RbpResultPreview
{
    internal const int MaximumVisitedWindows = 256;

    public static RbpResultPreview FromWindow(DynamicWindowResponse response, RbpResultPreview? previous = null)
    {
        var shares = new double?[CurrentBands.Length];
        var range = response.Range;
        if (response.State == "ready")
        {
            var result = response.Result ?? throw new InvalidOperationException("RBP 可用窗口缺少结果。");
            if (range is null || result.OutputKind != "algorithm_group" ||
                result.Axes is null || result.AxisUnits is null || result.Arrays is null || result.ArrayUnits is null ||
                result.Axes.Count != 2 || result.AxisUnits.Count != 2 || result.AxisUnits.GetValueOrDefault("time_s") != "s" ||
                result.AxisUnits.GetValueOrDefault("channel_index") != "index" ||
                !result.Axes.TryGetValue("time_s", out var time) || SingleValue(time) != range.EndSeconds ||
                !result.Axes.TryGetValue("channel_index", out var channelIndices) || channelIndices.ValueKind != JsonValueKind.Array ||
                result.Arrays.Count != CurrentBands.Length || result.ArrayUnits.Count != CurrentBands.Length)
                throw new InvalidOperationException("RBP 按需结果的字段、轴、范围或单位无效。");
            var channels = ReadChannelOrder(result);
            if (channelIndices.GetArrayLength() != channels.Length)
                throw new InvalidOperationException("RBP 按需结果的导联轴和导联顺序不一致。");
            var channelRows = new List<RbpChannelWindowPoint>();
            for (var index = 0; index < CurrentBands.Length; index++)
            {
                var name = CurrentBands[index].Key;
                if (!result.Arrays.TryGetValue(name, out var values) ||
                    result.ArrayUnits.GetValueOrDefault(name) != "ratio" ||
                    values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != channels.Length)
                    throw new InvalidOperationException($"RBP {name} 矩阵形状或单位无效。");
            }
            var failures = ReadChannelFailures(result.Evidence);
            var channelShares = channels.Select(_ => new double?[CurrentBands.Length]).ToArray();
            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                var values = CurrentBands.Select(band => result.Arrays[band.Key][channelIndex].ValueKind == JsonValueKind.Number &&
                    result.Arrays[band.Key][channelIndex].TryGetDouble(out var number) && double.IsFinite(number) ? (double?)number : null).ToArray();
                if (values.All(value => value is >= 0 and <= 1)) channelShares[channelIndex] = values;
                else if (!failures.ContainsKey(channels[channelIndex]))
                    throw new InvalidOperationException($"RBP {channels[channelIndex]} 缺少有效五频段值或失败证据。");
            }
            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                var previousPoint = previous?.ChannelWindows.Where(point => point.Channel == channels[channelIndex] &&
                    (point.Window.StartSeconds != range.StartSeconds || point.Window.EndSeconds != range.EndSeconds));
                channelRows.AddRange(previousPoint ?? []);
                channelRows.Add(new RbpChannelWindowPoint(channels[channelIndex], new RbpWindowPoint(range.EndSeconds,
                    range.StartSeconds, range.EndSeconds, "Complete", response.Quality ?? "clean",
                    failures.TryGetValue(channels[channelIndex], out var reason) ? reason : "", channelShares[channelIndex])));
            }
            shares = channelShares[0];
            previous = new RbpResultPreview(CurrentBands, shares, channelRows.Where(point => point.Channel == channels[0])
                .Select(point => point.Window).OrderBy(window => window.EndSeconds).TakeLast(MaximumVisitedWindows).ToArray(),
                response.Quality ?? "unknown", "", channelRows.OrderBy(point => point.Window.EndSeconds)
                    .TakeLast(MaximumVisitedWindows * channels.Length).ToArray());
        }
        var state = response.State == "ready" ? "Complete" : response.State switch
        {
            "rejected" => "Rejected", "unavailable" => "Unavailable", _ => response.State
        };
        if (response.Result?.Provenance is { ValueKind: JsonValueKind.Object } provenance &&
            provenance.TryGetProperty("analysis_state", out var declaredState) &&
            declaredState.GetString() is "Partial" or "Complete") state = declaredState.GetString()!;
        var failure = response.Failure is { } error
            ? $"{error.Code}：{error.Message}" + (error.Details is { } details ? $"\n{details.GetRawText()}" : "") : "";
        var windows = range is null ? previous?.Windows ?? [] :
            (previous?.Windows ?? []).Where(window => window.StartSeconds != range.StartSeconds || window.EndSeconds != range.EndSeconds)
            .Append(new RbpWindowPoint(range.EndSeconds, range.StartSeconds, range.EndSeconds,
                state, response.Quality ?? "unknown", failure, shares))
            .TakeLast(MaximumVisitedWindows).OrderBy(window => window.EndSeconds).ToArray();
        return new RbpResultPreview(CurrentBands, shares, windows, response.Quality ?? "unknown", failure, previous?.ChannelWindows);
    }

    private static double? SingleValue(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 1 &&
        array[0].ValueKind == JsonValueKind.Number && array[0].TryGetDouble(out var value) && double.IsFinite(value)
            ? value : null;

    private static string[] ReadChannelOrder(DynamicWindowResultResponse result)
    {
        if (result.Evidence is not { ValueKind: JsonValueKind.Object } evidence ||
            !evidence.TryGetProperty("extensions", out var extensions) ||
            !extensions.TryGetProperty("channel_order", out var values) || values.ValueKind != JsonValueKind.Object ||
            !values.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("RBP 按需结果缺少导联顺序证据。");
        return channels.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!).ToArray();
    }

    private static Dictionary<string, string> ReadChannelFailures(JsonElement? evidence)
    {
        if (evidence is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("extensions", out var extensions) ||
            !extensions.TryGetProperty("channel_failures", out var failures) || failures.ValueKind != JsonValueKind.Object ||
            !failures.TryGetProperty("by_channel", out failures) || failures.ValueKind != JsonValueKind.Object)
            return new(StringComparer.OrdinalIgnoreCase);
        return failures.EnumerateObject().ToDictionary(item => item.Name,
            item => item.Value.TryGetProperty("message", out var message) ? message.ToString() : "导联结果不可用",
            StringComparer.OrdinalIgnoreCase);
    }
}
