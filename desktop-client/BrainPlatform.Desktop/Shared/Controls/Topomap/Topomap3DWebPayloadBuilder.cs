using System.Globalization;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

internal static class Topomap3DWebPayloadBuilder
{
    internal static readonly JsonSerializerOptions CamelJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string BuildSceneStateJson(
        ElectrodeScalpModel3D scalp,
        IReadOnlyList<string> labels,
        TopomapField? field,
        TopomapColorScaleState? scaleState,
        bool isPreview,
        bool showDim,
        string? windowText)
    {
        var electrodes = new List<object>();
        object scaleInfo;
        var isRealField = field is not null;

        if (isRealField)
        {
            var rawValues = new Dictionary<string, (double Value, string DisplayLabel)>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in field!.Points)
            {
                if (point.Value is not { } val || !double.IsFinite(val)) continue;
                if (!string.IsNullOrWhiteSpace(point.Status) &&
                    !string.Equals(point.Status, "included", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(point.Status, "ok", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(point.Status, "valid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (var candidate in Topomap3DScenePreparer.CandidateSiteNames(point))
                {
                    rawValues.TryAdd(candidate, (val, candidate));
                }
            }

            var matched = new List<(ElectrodeLocation3D Site, string DisplayLabel, double Value)>();
            foreach (var group in scalp.ElectrodeLocations
                         .Where(site => rawValues.ContainsKey(site.Channel) || rawValues.ContainsKey(site.CanonicalChannel))
                         .GroupBy(site => site.CanonicalChannel, StringComparer.OrdinalIgnoreCase))
            {
                var site = group.First();
                var (val, displayLabel) = rawValues.TryGetValue(site.Channel, out var direct)
                    ? direct
                    : rawValues[site.CanonicalChannel];
                matched.Add((site, displayLabel, val));
            }

            var (scaleMin, scaleMax) = Topomap3DScenePreparer.ResolveFieldScaleBounds(
                field, matched.Select(item => item.Value).ToArray(), scaleState);
            var span = scaleMax - scaleMin;
            foreach (var (site, displayLabel, val) in matched)
            {
                var norm = span > 1e-12 ? Math.Clamp((val - scaleMin) / span, 0d, 1d) : 0.5;
                electrodes.Add(new
                {
                    label = displayLabel,
                    canonical = site.CanonicalChannel,
                    x = site.CoordinateGnm[0],
                    y = site.CoordinateGnm[1],
                    z = site.CoordinateGnm[2],
                    valueNorm = norm,
                });
            }

            var mid = (scaleMin + scaleMax) * 0.5;
            scaleInfo = new
            {
                unit = FormatUnitLabel(field.Unit),
                minText = FormatScaleTick(scaleMin, field.Unit),
                midText = FormatScaleTick(mid, field.Unit),
                maxText = FormatScaleTick(scaleMax, field.Unit),
            };
        }
        else
        {
            var selected = labels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var group in scalp.ElectrodeLocations
                         .Where(site => selected.Contains(site.Channel) || selected.Contains(site.CanonicalChannel))
                         .GroupBy(site => site.CanonicalChannel, StringComparer.OrdinalIgnoreCase))
            {
                var site = group.First();
                var displayLabel = labels.FirstOrDefault(label =>
                    string.Equals(label?.Trim(), site.Channel, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(label?.Trim(), site.CanonicalChannel, StringComparison.OrdinalIgnoreCase))?.Trim()
                    ?? site.Channel;
                electrodes.Add(new
                {
                    label = displayLabel,
                    canonical = site.CanonicalChannel,
                    x = site.CoordinateGnm[0],
                    y = site.CoordinateGnm[1],
                    z = site.CoordinateGnm[2],
                });
            }

            scaleInfo = new
            {
                unit = "示意强度",
                minText = "0.0 低",
                midText = "0.5 中",
                maxText = "1.0 高",
            };
        }

        return JsonSerializer.Serialize(new
        {
            type = "setSceneState",
            isRealField,
            isPreviewHeatmap = isPreview,
            showDimSwitcher = showDim,
            windowText = string.IsNullOrWhiteSpace(windowText) ? null : windowText.Trim(),
            scaleInfo,
            electrodes,
        }, CamelJsonOptions);
    }

    private static string FormatUnitLabel(string? unit) => unit switch
    {
        "ratio" => "相对功率 (%)",
        "uV^2" => "µV²",
        "V^2/Hz" => "V²/Hz",
        "dB" => "dB",
        null or "" => "强度",
        _ => unit,
    };

    private static string FormatScaleTick(double value, string? unit)
    {
        if (string.Equals(unit, "ratio", StringComparison.OrdinalIgnoreCase))
            return $"{value * 100d:0.#}%";
        if (Math.Abs(value) >= 100 || (Math.Abs(value) < 0.01 && value != 0))
            return value.ToString("G3", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
