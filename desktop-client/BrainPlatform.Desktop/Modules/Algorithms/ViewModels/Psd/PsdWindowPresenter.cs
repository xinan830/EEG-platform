using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

/// <summary>One backend window, not a fabricated full Run or structured artifact.</summary>
internal static class PsdWindowPresenter
{
    public static void Apply(PsdPreviewState state, DynamicWindowResponse response)
    {
        state.Clear();
        var result = response.Result;
        if (response.State == "ready")
        {
            if (result?.Axes is null || result.Arrays is null || result.ArrayUnits is null ||
                !result.Axes.TryGetValue("frequency_hz", out var frequencies) ||
                !result.Arrays.TryGetValue("psd", out var values) ||
                frequencies.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array ||
                frequencies.GetArrayLength() != values.GetArrayLength() ||
                !result.ArrayUnits.TryGetValue("psd", out var unit) || unit is not ("V^2/Hz" or "uV^2/Hz"))
                throw new InvalidOperationException("PSD 窗口数组、轴或单位不符合展示契约。");
            if (result.AxisUnits?.GetValueOrDefault("frequency_hz") != "Hz")
                throw new InvalidOperationException("PSD 频率轴必须显式声明 Hz。");
            if (result.Arrays.TryGetValue("band_share", out var bandShares) &&
                (bandShares.ValueKind != JsonValueKind.Array || bandShares.GetArrayLength() != 5 ||
                 result.ArrayUnits.GetValueOrDefault("band_share") != "ratio"))
                throw new InvalidOperationException("PSD 频段占比的形状或单位不符合展示契约。");
            state.FrequencyUnit = "Hz";
            state.ValueUnit = "uV^2/Hz";
            PsdPointProjection.Apply(state, frequencies.EnumerateArray().ToArray(),
                values.EnumerateArray().ToArray(), unit == "V^2/Hz");
            if (result.Arrays.TryGetValue("band_share", out var shares) && shares.ValueKind == JsonValueKind.Array)
            {
                var labels = new[] { ("Delta", "1–4 Hz", "#4F8EDC"), ("Theta", "4–8 Hz", "#56B88A"),
                    ("Alpha", "8–13 Hz", "#E77B88"), ("Beta", "13–30 Hz", "#D99A38"), ("Gamma", "30–50 Hz", "#8D73D1") };
                for (var index = 0; index < Math.Min(labels.Length, shares.GetArrayLength()); index++)
                {
                    var label = labels[index];
                    var item = shares[index];
                    var available = item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number);
                    var value = available ? item.GetDouble() : 0;
                    state.BandShares.Add(new PsdBandSharePoint(label.Item1, label.Item2, value,
                        available ? $"{value:P1}" : "不可用", label.Item3, available));
                }
            }
        }
        state.WindowPreview = response;
    }
}
