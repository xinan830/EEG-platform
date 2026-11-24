using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public static class ReviewScalarProjectors
{
    public static IReadOnlyList<ReviewScalarPoint> Faa(DynamicWindowResponse response, string? _, string? __) =>
        FaaResultPreview.FromWindow(response).DynamicPoints.Select(point =>
            Point(response, point.TimeSeconds, point.StartSeconds, point.EndSeconds,
                point.Value, point.State, point.Quality, point.Failure)).ToArray();

    public static IReadOnlyList<ReviewScalarPoint> Iapf(DynamicWindowResponse response, string? channel, string? _) =>
        IapfResultPreview.FromWindow(response, channel ?? "").DynamicPoints.Select(point =>
            Point(response, point.TimeSeconds, point.StartSeconds, point.EndSeconds,
                point.ValueHz, point.State, point.Quality, point.Failure)).ToArray();

    public static IReadOnlyList<ReviewScalarPoint> ThetaBeta(DynamicWindowResponse response, string? channel, string? _) =>
        ThetaBetaResultPreview.FromWindow(response, channel ?? "").DynamicPoints.Select(point =>
            Point(response, point.TimeSeconds, point.StartSeconds, point.EndSeconds,
                point.Value, point.State, point.Quality, point.Failure)).ToArray();

    public static IReadOnlyList<ReviewScalarPoint> Brainbeat(DynamicWindowResponse response, string? fz, string? pz) =>
        BrainbeatResultPreview.FromWindow(response, $"{fz} / {pz}",
            algorithmId: BrainbeatResultPreview.VariantId(response)).Points.Select(point =>
            Point(response, point.TimeSeconds, point.StartSeconds, point.EndSeconds,
                point.Value, point.State, point.Quality, point.Failure)).ToArray();

    private static ReviewScalarPoint Point(DynamicWindowResponse response, double timeSeconds,
        double startSeconds, double endSeconds, double? value, string state, string quality, string failure) =>
        new(timeSeconds, startSeconds, endSeconds, value, state, quality, failure,
            response.Result?.Evidence, response.Result?.Provenance);
}
