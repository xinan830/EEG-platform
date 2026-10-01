using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Faa;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Iapf;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal static class AlgorithmRunResultHandlerRegistry
{
    private static readonly IReadOnlyDictionary<string, IAlgorithmRunResultHandler> handlers =
        new IAlgorithmRunResultHandler[]
        {
            new PsdRunResultHandler(),
            new StftRunResultHandler(),
            new RbpRunResultHandler(),
            new PeakFrequencyRunResultHandler(),
            new IapfRunResultHandler(),
            new BandRatioRunResultHandler(),
            new BrainbeatRunResultHandler(),
            new FaaRunResultHandler(),
            new ThetaBetaRunResultHandler(),
        }.ToDictionary(handler => handler.AlgorithmId, StringComparer.OrdinalIgnoreCase);

    internal static IAlgorithmRunResultHandler For(string algorithmId) =>
        handlers.TryGetValue(algorithmId, out var handler)
            ? handler
            : throw new InvalidOperationException($"算法 {algorithmId} 尚未注册 WPF 运行模块。");

    internal static bool Contains(string algorithmId) => handlers.ContainsKey(algorithmId);
}
