using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

public sealed partial class PsdDetailViewModel
{
    private DynamicWindowResponse? WindowResult => catalog.PsdPreviewState.WindowPreview;

    private static string WindowFailureDetails(DynamicWindowResponse response)
    {
        if (response.Failure is not { } failure) return "当前窗口没有失败证据。";
        JsonElement? quality = null;
        if (response.Result?.Evidence is { ValueKind: JsonValueKind.Object } evidence &&
            evidence.TryGetProperty("source_quality", out var sourceQuality)) quality = sourceQuality;
        if (quality is null && failure.Details is { ValueKind: JsonValueKind.Object } rawQuality &&
            (rawQuality.TryGetProperty("gate_failed", out _) || rawQuality.TryGetProperty("clean_ratio", out _)))
            quality = rawQuality;
        var details = quality is not null ? JsonSerializer.SerializeToElement(new { quality }) : failure.Details;
        return BuildRunErrorDetails(new StructuredRunError(failure.Code, failure.Message, "dynamic_window", details));
    }
}
