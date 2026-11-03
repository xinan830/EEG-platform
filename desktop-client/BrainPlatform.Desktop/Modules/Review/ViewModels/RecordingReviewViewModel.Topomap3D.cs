using System.Diagnostics;
using System.IO;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private CancellationTokenSource? topomap3DCancellation;

    private async Task RefreshRbpTopomap3DAsync()
    {
        topomapCancellation?.Cancel();
        topomap3DCancellation?.Cancel();
        topomap3DCancellation?.Dispose();
        topomap3DCancellation = new CancellationTokenSource();
        var owner = topomap3DCancellation;
        var token = owner.Token;
        var window = CurrentAnalysisWindow;
        if (window is null || analysisCoordinator is null)
        {
            RbpAnalysis.ClearTopomap3D();
            return;
        }

        try
        {
            var timer = Stopwatch.StartNew();
            var result = await Task.Run(() => analysisCoordinator.LoadTopomap3DAsync(window, token,
                RbpAnalysis.SelectedTopomapPowerType, RbpAnalysis.SelectedTopomap3DBandId), token);
            if (token.IsCancellationRequested || !ReferenceEquals(topomap3DCancellation, owner) || result is null)
                return;
            var expectedWindow = window.DynamicResponse?.WindowId ?? window.RunId;
            if (result.Result.RunId != expectedWindow || result.Result.RecordingId != window.RecordingId)
                throw new InvalidDataException("三维地形图的录音或窗口身份与当前分析窗口不一致。");
            if (!RbpAnalysis.ApplyTopomap3DIfAvailable(result)) return;
            if (DesktopLog.PerformanceDiagnosticsEnabled)
                DesktopLog.Info($"review_topomap_3d_perf stage=fetch window_id={expectedWindow} " +
                    $"cursor_sample={window.CursorSample} vertices={result.Model.VerticesGnm.Count} " +
                    $"faces={result.Model.Triangles.Count} elapsed_ms={timer.Elapsed.TotalMilliseconds:0.0}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (!token.IsCancellationRequested && ReferenceEquals(topomap3DCancellation, owner))
        {
            RbpAnalysis.SetStatus($"三维地形图读取失败：{exception.Message}");
        }
    }
}
