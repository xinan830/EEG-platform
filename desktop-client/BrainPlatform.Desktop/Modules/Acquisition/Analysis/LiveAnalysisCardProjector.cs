using System.Text.Json;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

internal static class LiveAnalysisCardProjector
{
    internal static readonly IReadOnlyList<string> RbpTopomapFieldIds =
    [
        "rbp.relative.delta", "rbp.relative.theta", "rbp.relative.alpha", "rbp.relative.beta", "rbp.relative.gamma",
        "rbp.absolute.delta", "rbp.absolute.theta", "rbp.absolute.alpha", "rbp.absolute.beta", "rbp.absolute.gamma",
    ];

    internal static void Apply(LiveAnalysisCardViewModel card, LiveAnalysisUpdate update)
    {
        var status = FormatStatus(update, card.AlgorithmId);
        card.StatusText = status;
        try
        {
            if (card.PanelKind == LiveAnalysisPanelKind.Rbp && card.RbpAnalysis is { } rbp)
            {
                if (update.AlgorithmId == "rbp" && LiveRbpPresentation.Read(update, card.Channels) is { } rbpUpdate)
                    rbp.Apply(rbpUpdate);
                else if (update.AlgorithmId == "rbp-topomap")
                {
                    if (update.State is not ("ready" or "partial")) rbp.ClearTopomap();
                    else rbp.ApplyTopomap(LiveSpectralPresentation.ReadMaps(update, RbpTopomapFieldIds), update.StartSeconds, update.EndSeconds);
                }
                return;
            }
            if (card.PanelKind == LiveAnalysisPanelKind.Psd && card.PsdAnalysis is { } psd)
            {
                if (update.AlgorithmId == "psd-topomap")
                {
                    if (update.State is not ("ready" or "partial")) psd.ClearTopomap();
                    else psd.ApplyTopomap(update, LiveSpectralPresentation.ReadMaps(update, LivePsdAnalysisViewModel.TopomapFieldIds));
                }
                else if (card.SelectedChannel is { } channel)
                {
                    psd.ApplySpectrum(update, channel, status);
                }
                return;
            }
            if (card.PanelKind == LiveAnalysisPanelKind.Stft && card.StftAnalysis is { } stft && card.SelectedChannel is { } stftChannel)
                stft.Apply(update, stftChannel, status);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            const string formatError = "实时分析结果格式不匹配";
            card.StatusText = formatError;
            card.PsdAnalysis?.SetFault(formatError);
            card.StftAnalysis?.SetFault(formatError);
            DesktopLog.Warning($"live_analysis_projection_invalid session={update.SessionId} algorithm={update.AlgorithmId} revision={update.Revision} {exception.Message}");
        }
    }

    private static string FormatStatus(LiveAnalysisUpdate update, string cardAlgorithmId) => update.State switch
    {
        "ready" => "",
        "partial" when cardAlgorithmId == "rbp" => "",
        "partial" => "部分数据可用",
        "warming_up" => "等待可用的 10 秒窗口",
        _ => update.Message ?? "当前窗口不可用",
    };
}
