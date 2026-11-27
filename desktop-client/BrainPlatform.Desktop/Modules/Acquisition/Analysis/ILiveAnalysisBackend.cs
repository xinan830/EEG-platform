using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Backend boundary for live Acquisition window algorithms, enabling unit testing without hardware or HTTP.</summary>
public interface ILiveAnalysisBackend
{
    AcquisitionStateSnapshot State { get; }
    AcquisitionStreamMetadata? StreamMetadata { get; }
    LiveMontageSnapshot? LiveMontage { get; }
    double? CurrentElapsedSeconds { get; }
    event EventHandler<LiveAnalysisUpdate>? LiveAnalysisUpdated;
    event EventHandler<AcquisitionFault>? LiveAnalysisFaulted;
    event Action<LiveMontageSnapshot>? LiveMontageChanged;
    Task SetLiveAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken);
}

internal sealed class RuntimeLiveAnalysisBackend(ConfiguredAcquisitionRuntime runtime) : ILiveAnalysisBackend
{
    public AcquisitionStateSnapshot State => runtime.State;
    public AcquisitionStreamMetadata? StreamMetadata => runtime.StreamMetadata;
    public LiveMontageSnapshot? LiveMontage => runtime.LiveMontage;
    public double? CurrentElapsedSeconds => runtime.CurrentElapsedSeconds;

    public event EventHandler<LiveAnalysisUpdate>? LiveAnalysisUpdated
    {
        add => runtime.LiveAnalysisUpdated += value;
        remove => runtime.LiveAnalysisUpdated -= value;
    }

    public event EventHandler<AcquisitionFault>? LiveAnalysisFaulted
    {
        add => runtime.LiveAnalysisFaulted += value;
        remove => runtime.LiveAnalysisFaulted -= value;
    }

    public event Action<LiveMontageSnapshot>? LiveMontageChanged
    {
        add => runtime.LiveMontageChanged += value;
        remove => runtime.LiveMontageChanged -= value;
    }

    public Task SetLiveAlgorithmEnabledAsync(string algorithmId, bool enabled, JsonElement config, CancellationToken cancellationToken) =>
        runtime.SetLiveAlgorithmEnabledAsync(algorithmId, enabled, config, cancellationToken);
}
