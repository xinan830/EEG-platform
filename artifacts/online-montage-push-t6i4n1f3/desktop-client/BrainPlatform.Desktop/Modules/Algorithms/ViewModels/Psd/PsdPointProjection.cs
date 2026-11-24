using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

/// <summary>Shared display projection of backend spectrum points, with explicit unit conversion.</summary>
internal static class PsdPointProjection
{
    public static void Apply(PsdPreviewState state, IReadOnlyList<JsonElement> frequencies,
        IReadOnlyList<JsonElement> values, bool convertFromVolts)
    {
        state.Points.Clear();
        for (var index = 0; index < Math.Min(frequencies.Count, values.Count); index++)
        {
            if (frequencies[index].ValueKind != JsonValueKind.Number ||
                values[index].ValueKind != JsonValueKind.Number ||
                !frequencies[index].TryGetDouble(out var frequency) ||
                !values[index].TryGetDouble(out var value) ||
                !double.IsFinite(frequency) || !double.IsFinite(value))
            {
                state.Points.Add(null);
                continue;
            }
            state.Points.Add(new PsdPreviewPoint(frequency, convertFromVolts ? value * 1_000_000_000_000d : value));
        }
        state.HasPreview = state.Points.Any(point => point is not null);
        var available = state.Points.OfType<PsdPreviewPoint>().ToArray();
        state.FrequencyRangeText = available.Length == 0 ? "" :
            $"{available.Min(point => point.Frequency):0.###} - {available.Max(point => point.Frequency):0.###} {state.FrequencyUnit}";
        state.ValueRangeText = available.Length == 0 ? "" :
            $"{available.Min(point => point.Value):G3} - {available.Max(point => point.Value):G3} {state.ValueUnit}";
    }
}
