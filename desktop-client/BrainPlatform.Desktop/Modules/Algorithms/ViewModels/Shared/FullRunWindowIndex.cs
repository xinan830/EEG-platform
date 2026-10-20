using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>Immutable absolute-sample index for a validated structured result.</summary>
internal sealed class FullRunWindowIndex
{
    private readonly IReadOnlyList<JsonElement> windows;

    private FullRunWindowIndex(IReadOnlyList<JsonElement> windows) => this.windows = windows;

    public static FullRunWindowIndex Create(IReadOnlyList<JsonElement> windows) =>
        new(windows.ToArray());

    public (int Index, JsonElement Window)? Find(long cursorSample)
    {
        var low = 0;
        var high = windows.Count - 1;
        var selected = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var end = EndSample(windows[middle]);
            if (end <= cursorSample)
            {
                selected = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        return selected < 0 ? null : (selected, windows[selected]);
    }

    private static long EndSample(JsonElement window) =>
        window.TryGetProperty("end_sample", out var value) && value.TryGetInt64(out var end)
            ? end
            : throw new InvalidOperationException("完整结果窗口缺少 end_sample。");
}
