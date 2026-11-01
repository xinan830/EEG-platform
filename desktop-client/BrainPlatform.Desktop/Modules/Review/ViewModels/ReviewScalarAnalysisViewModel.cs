using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed record ReviewScalarPoint(double TimeSeconds, double StartSeconds, double EndSeconds,
    double? Value, string State, string Quality, string Failure,
    System.Text.Json.JsonElement? Evidence, System.Text.Json.JsonElement? Provenance);

/// <summary>Review-only chart projection; all values and validation come from the algorithm module.</summary>
public sealed class ReviewScalarAnalysisViewModel(string unit,
    Func<DynamicWindowResponse, string?, string?, IReadOnlyList<ReviewScalarPoint>> project) : ObservableObject
{
    private IReadOnlyList<ReviewScalarPoint> points = [];
    private string statusText = "尚未准备。";
    private string sourceText = "";

    public IReadOnlyList<ReviewScalarPoint> Points { get => points; private set => SetProperty(ref points, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string SourceText { get => sourceText; private set => SetProperty(ref sourceText, value); }
    public string Unit { get; } = unit;

    public void Apply(WindowPreviewResponse? response, string? primaryChannel, string? secondaryChannel)
    {
        if (response is null)
        {
            Points = [];
            SourceText = "";
            StatusText = "尚未准备。";
            return;
        }

        if (response.DynamicResponse is not { } raw)
        {
            Points = [];
            StatusText = "缺少可验证的算法结果。";
            return;
        }

        try
        {
            var incoming = project(raw, primaryChannel, secondaryChannel);
            Points = points.Where(existing => incoming.All(point => point.TimeSeconds != existing.TimeSeconds))
                .Concat(incoming).OrderBy(point => point.TimeSeconds).TakeLast(256).ToArray();
            SourceText = secondaryChannel is null ? primaryChannel ?? "" : $"{primaryChannel} / {secondaryChannel}";
            var currentFailure = incoming.LastOrDefault()?.Failure;
            StatusText = response.Status == "available" ? "" :
                !string.IsNullOrWhiteSpace(currentFailure) ? currentFailure :
                response.ReasonCodes.Count == 0 ? $"窗口状态：{response.Status}" : string.Join("、", response.ReasonCodes);
        }
        catch (InvalidOperationException exception)
        {
            Points = [];
            StatusText = $"结果无法展示：{exception.Message}";
        }
    }

}
