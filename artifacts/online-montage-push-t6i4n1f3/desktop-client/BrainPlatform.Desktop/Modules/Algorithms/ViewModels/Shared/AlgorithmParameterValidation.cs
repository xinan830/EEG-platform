using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal static class AlgorithmParameterValidation
{
    internal static void ValidateOrThrow(AlgorithmCatalogItem algorithm, JsonElement config)
    {
        foreach (var parameter in algorithm.Parameters)
        {
            if (!config.TryGetProperty(parameter.Key, out var field) ||
                field.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (field.ValueKind != JsonValueKind.Number) continue;
            var value = field.GetDouble();
            var label = string.IsNullOrWhiteSpace(parameter.LabelZh) ? parameter.Key : parameter.LabelZh;
            var unit = string.IsNullOrWhiteSpace(parameter.Unit) ? "" : $" {parameter.Unit}";
            if (!double.IsFinite(value))
                throw new InvalidOperationException($"{label}必须是有限数值。");
            if (parameter.Minimum is double minimum && value < minimum)
                throw new InvalidOperationException($"{label}不能小于 {Format(minimum)}{unit}，当前为 {Format(value)}{unit}。");
            if (parameter.Maximum is double maximum && value > maximum)
                throw new InvalidOperationException($"{label}不能大于 {Format(maximum)}{unit}，当前为 {Format(value)}{unit}。");
        }
    }

    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
