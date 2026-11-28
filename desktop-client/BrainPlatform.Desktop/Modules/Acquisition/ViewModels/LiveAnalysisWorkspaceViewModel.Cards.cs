using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Card creation, backend request construction, and presentation reset for live analysis slots.</summary>
public sealed partial class LiveAnalysisWorkspaceViewModel
{
    private LiveAnalysisCardViewModel CreateCard(string algorithmId, Guid sessionId, IReadOnlyList<string> channels)
    {
        var initialChannel = channels.FirstOrDefault();
        var card = algorithmId switch
        {
            "rbp" => CreateRbpCard(sessionId, channels),
            "psd" => CreatePsdCard(channels, initialChannel),
            "stft" => CreateStftCard(channels, initialChannel),
            "iapf" or "theta_beta" or "brainbeat" => CreateScalarCard(algorithmId, channels, initialChannel),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmId), algorithmId, "不支持的实时算法卡片。"),
        };
        card.SessionId = sessionId;
        return card;
    }

    private LiveAnalysisCardViewModel CreateRbpCard(Guid sessionId, IReadOnlyList<string> channels)
    {
        var rbp = new LiveRbpAnalysisViewModel();
        rbp.Begin(sessionId, backend.CurrentElapsedSeconds);
        return new LiveAnalysisCardViewModel(this, "rbp", "RBP 相对频段功率", "RBP",
            LiveAnalysisPanelKind.Rbp, channels, null, null, rbpAnalysis: rbp);
    }

    private LiveAnalysisCardViewModel CreatePsdCard(IReadOnlyList<string> channels, string? initialChannel)
    {
        LiveAnalysisCardViewModel? card = null;
        var psd = new LivePsdAnalysisViewModel(vm =>
        {
            if (card is { IsClosed: false }) _ = SafeReconfigureAsync(card);
        });
        psd.Begin();
        card = new LiveAnalysisCardViewModel(this, "psd", "PSD 功率谱密度", "PSD",
            LiveAnalysisPanelKind.Psd, channels, initialChannel, c => _ = SafeReconfigureAsync(c), psdAnalysis: psd);
        return card;
    }

    private LiveAnalysisCardViewModel CreateStftCard(IReadOnlyList<string> channels, string? initialChannel)
    {
        var stft = new LiveStftAnalysisViewModel();
        stft.Begin(backend.CurrentElapsedSeconds);
        return new LiveAnalysisCardViewModel(this, "stft", "STFT 时频图", "STFT",
            LiveAnalysisPanelKind.Stft, channels, initialChannel, c => _ = SafeReconfigureAsync(c), stftAnalysis: stft);
    }

    private LiveAnalysisCardViewModel CreateScalarCard(string algorithmId, IReadOnlyList<string> channels, string? initialChannel)
    {
        var (displayName, headerTitle, unit, isPaired) = algorithmId switch
        {
            "iapf" => ("IAPF 个体 Alpha 峰频率", "IAPF", "Hz", false),
            "theta_beta" => ("Theta/Beta 比值", "Theta/Beta", "dimensionless", false),
            "brainbeat" => ("经典 BrainBeat", "BrainBeat", "ratio", true),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmId)),
        };
        LiveAnalysisCardViewModel? card = null;
        var scalar = new LiveScalarAnalysisViewModel(algorithmId, unit, 20d, vm =>
        {
            if (card is { IsClosed: false }) _ = SafeReconfigureAsync(card);
        });
        scalar.Begin(backend.CurrentElapsedSeconds);
        var montage = backend.LiveMontage;
        var primaryChannels = isPaired
            ? LiveScalarPresentation.BrainbeatChannelOptions(channels, montage, "Fz")
            : channels;
        var secondaryChannels = isPaired
            ? LiveScalarPresentation.BrainbeatChannelOptions(channels, montage, "Pz")
            : null;
        var (primary, secondary) = isPaired
            ? LiveScalarPresentation.ResolveBrainbeatChannels(channels, montage)
            : (initialChannel, null);
        card = new LiveAnalysisCardViewModel(
            this, algorithmId, displayName, headerTitle, LiveAnalysisPanelKind.Scalar,
            primaryChannels, primary, c => _ = SafeReconfigureAsync(c),
            scalarAnalysis: scalar, windowSeconds: 20d, hasSecondaryChannel: isPaired,
            initialSecondaryChannel: secondary,
            primaryChannelToolTip: isPaired ? "Fz 来源通道" : "分析通道",
            secondaryChannelToolTip: "Pz 来源通道",
            secondaryChannelOptions: secondaryChannels);
        return card;
    }

    private IReadOnlyList<(string BackendId, JsonElement Config, bool Optional)> BuildBackendRequests(
        LiveAnalysisCardViewModel card, AcquisitionStreamMetadata stream)
    {
        var montage = backend.LiveMontage;
        if (card.AlgorithmId == "rbp")
        {
            return
            [
                ("rbp", LiveRbpPresentation.Config(card.Channels), false),
                ("rbp-topomap", LiveSpectralPresentation.MultiBandMapConfig(stream, montage, LiveAnalysisCardProjector.RbpTopomapFieldIds), true),
            ];
        }
        if (card.AlgorithmId == "psd" && card.PsdAnalysis is { IsTopomapSelected: true })
        {
            return
            [
                ("psd-topomap", LiveSpectralPresentation.MultiBandMapConfig(stream, montage, LivePsdAnalysisViewModel.TopomapFieldIds), false),
            ];
        }
        if (card.PanelKind == LiveAnalysisPanelKind.Scalar && card.ScalarAnalysis is { } scalar)
        {
            if (card.HasSecondaryChannel)
            {
                if (string.IsNullOrWhiteSpace(card.SelectedChannel) || string.IsNullOrWhiteSpace(card.SecondaryChannel) ||
                    string.Equals(card.SelectedChannel, card.SecondaryChannel, StringComparison.OrdinalIgnoreCase))
                {
                    const string pairError = "请选择互不相同的 Fz 与 Pz 来源通道";
                    scalar.SetUnavailableConfiguration(pairError);
                    card.StatusText = pairError;
                    return [];
                }
                return [(card.AlgorithmId, LiveScalarPresentation.Config(
                    card.AlgorithmId, [card.SelectedChannel, card.SecondaryChannel],
                    card.WindowSeconds, 1d, 50d, scalar.FrequencyMode), false)];
            }
            var channel = card.SelectedChannel ?? card.Channels.FirstOrDefault()
                ?? throw new InvalidOperationException("请选择分析通道。");
            return [(card.AlgorithmId, LiveScalarPresentation.Config(
                card.AlgorithmId, [channel], card.WindowSeconds, 1d, 50d, scalar.FrequencyMode), false)];
        }
        var selectedChannel = card.SelectedChannel ?? card.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("请选择分析通道。");
        return [(card.AlgorithmId, LiveRbpPresentation.Config([selectedChannel]), false)];
    }

    private void ResetCardPresentationBeforeRun(LiveAnalysisCardViewModel card, Guid sessionId)
    {
        card.SessionId = sessionId;
        var warmup = $"等待可用的 {card.WindowSeconds:0} 秒窗口";
        card.StatusText = warmup;
        card.RbpAnalysis?.Begin(sessionId, backend.CurrentElapsedSeconds);
        card.PsdAnalysis?.Begin();
        card.StftAnalysis?.Begin(backend.CurrentElapsedSeconds);
        card.ScalarAnalysis?.Begin(backend.CurrentElapsedSeconds, warmup);
    }
}
