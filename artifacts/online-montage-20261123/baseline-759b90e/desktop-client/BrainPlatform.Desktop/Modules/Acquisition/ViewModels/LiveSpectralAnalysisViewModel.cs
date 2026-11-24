using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed record LiveSpectralOption(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Owns the temporary analysis panel and its one active view for the capture session.</summary>
public sealed class LiveSpectralAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly Dispatcher dispatcher;
    private Selection? active;
    private long generation;
    private long revision = -1;
    private long topomapRevision = -1;
    private bool disposed;
    private bool isOpen;
    private bool isBusy;
    private string status = "选择图表";
    private string window = "";
    private string band = "alpha";
    private StftPreview? stft;
    private TopomapField? topomapField;
    private double? analysisStartSeconds;
    private string? channel;
    private IReadOnlyList<string> channels = [];
    private LiveSpectralOption selectedView = Views[0];

    public LiveSpectralAnalysisViewModel(ConfiguredAcquisitionRuntime runtime, Dispatcher dispatcher, Action<Exception> reportError)
    {
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, reportError);
        OpenRbpCommand = new AsyncRelayCommand(() => OpenViewAsync("rbp"), reportError);
        OpenPsdCommand = new AsyncRelayCommand(() => OpenViewAsync("psd"), reportError);
        OpenStftCommand = new AsyncRelayCommand(() => OpenViewAsync("stft"), reportError);
        OpenRbpTopomapCommand = new AsyncRelayCommand(() => OpenViewAsync("rbp-topomap"), reportError);
        OpenPsdTopomapCommand = new AsyncRelayCommand(() => OpenViewAsync("psd-topomap"), reportError);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, reportError);
        runtime.LiveAnalysisUpdated += OnUpdate;
        runtime.LiveAnalysisFaulted += OnFault;
        runtime.LiveMontageChanged += OnMontageChanged;
    }

    public static IReadOnlyList<LiveSpectralOption> Views { get; } =
        [new("rbp", "RBP 趋势"), new("psd", "功率谱密度"), new("stft", "时频图"),
         new("rbp-topomap", "RBP 地形图"), new("psd-topomap", "PSD 地形图")];
    public IReadOnlyList<LiveSpectralOption> PsdViewOptions { get; } =
        [new("psd", "频谱"), new("psd-topomap", "脑电地形图")];
    public IReadOnlyList<LiveSpectralOption> PsdTopomapBandOptions { get; } =
        [new("delta", "Delta 波"), new("theta", "Theta 波"), new("alpha", "Alpha 波"),
         new("beta", "Beta 波"), new("gamma", "Gamma 波")];
    public IReadOnlyList<LiveSpectralOption> ViewOptions => Views;
    public IReadOnlyList<string> Bands { get; } = ["delta", "theta", "alpha", "beta", "gamma"];
    public IReadOnlyList<LiveSpectralOption> PowerOptions { get; } =
        [new("relative", "相对功率"), new("absolute", "绝对功率"), new("amplitude", "RMS 振幅")];
    public IReadOnlyList<string> Channels { get => channels; private set => SetProperty(ref channels, value); }
    public string? Channel
    {
        get => channel;
        set
        {
            if (!SetProperty(ref channel, value) || !isOpen || SelectedView.Id is not ("psd" or "stft")) return;
            ApplyCommand.Execute(null);
        }
    }
    public string Band
    {
        get => band;
        set
        {
            if (!SetProperty(ref band, value) || !isOpen || SelectedView.Id != "psd-topomap") return;
            ApplyCommand.Execute(null);
        }
    }
    public string Power { get; set; } = "relative";
    public LiveSpectralOption SelectedView
    {
        get => selectedView;
        set
        {
            if (value is null || !SetProperty(ref selectedView, value)) return;
            RaisePropertyChanged(nameof(NeedsChannel));
            RaisePropertyChanged(nameof(NeedsBand));
            RaisePropertyChanged(nameof(NeedsPower));
            RaisePropertyChanged(nameof(SelectedPsdView));
        }
    }

    public LiveSpectralOption SelectedPsdView
    {
        get => PsdViewOptions.First(option => option.Id == (SelectedView.Id == "psd-topomap" ? "psd-topomap" : "psd"));
        set
        {
            if (value is null || value.Id == SelectedView.Id) return;
            SelectedView = Views.First(option => option.Id == value.Id);
            if (isOpen) ApplyCommand.Execute(null);
        }
    }

    public bool NeedsChannel => SelectedView.Id is "stft" or "psd";
    public bool NeedsBand => SelectedView.Id is "rbp-topomap" or "psd-topomap";
    public bool NeedsPower => SelectedView.Id == "rbp-topomap";
    public string? ActiveView => active?.View.Id;
    public string? ActiveLabel => active is { } view ? view.View.Label +
        (view.Channel is { } name ? $" · {name}" : view.Band is { } band ? $" · {band}" : "") : null;
    public bool IsOpen { get => isOpen; private set => SetProperty(ref isOpen, value); }
    public bool IsSessionReady => runtime.State.State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused;
    public bool IsInteractive => IsSessionReady && !isBusy;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Window { get => window; private set => SetProperty(ref window, value); }
    public double? AnalysisStartSeconds
    {
        get => analysisStartSeconds;
        private set => SetProperty(ref analysisStartSeconds, value);
    }
    public StftPreview? Stft { get => stft; private set => SetProperty(ref stft, value); }
    public TopomapField? Field { get => topomapField; private set => SetProperty(ref topomapField, value); }
    public PsdPreviewState Psd { get; } = new() { ValueUnit = "µV²/Hz" };
    public LiveRbpAnalysisViewModel Rbp { get; } = new();
    public TopomapColorScaleState ColorScales { get; } = new();
    public ICommand ToggleCommand { get; }
    public ICommand OpenRbpCommand { get; }
    public ICommand OpenPsdCommand { get; }
    public ICommand OpenStftCommand { get; }
    public ICommand OpenRbpTopomapCommand { get; }
    public ICommand OpenPsdTopomapCommand { get; }
    public ICommand ApplyCommand { get; }

    public void RefreshSession()
    {
        if (!IsSessionReady || (active is { } current && runtime.State.SessionId != current.Session))
        {
            IsOpen = false;
            SetActive(null, "选择图表");
        }
        RaisePropertyChanged(nameof(IsInteractive));
    }

    private void OnMontageChanged(LiveMontageSnapshot montage)
    {
        var switchGeneration = Interlocked.Increment(ref generation);
        _ = dispatcher.InvokeAsync(async () =>
        {
            if (disposed || !IsSessionReady || switchGeneration != Interlocked.Read(ref generation)) return;
            var previous = active;
            Channels = LiveSpectralPresentation.AnalysisChannelNames(montage);
            var nextChannel = montage.Profile.DerivedChannels.FirstOrDefault(output =>
                string.Equals(output.Name, Channel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(output.PositiveLabel, Channel, StringComparison.OrdinalIgnoreCase))?.Name ?? Channels.FirstOrDefault();
            SetProperty(ref channel, nextChannel, nameof(Channel));
            if (!IsOpen || previous is null) return;
            SetActive(null, "导联已切换，等待新窗口");
            var restartGeneration = Interlocked.Read(ref generation);
            try
            {
                await runtime.SetLiveAlgorithmEnabledAsync(previous.View.Id, false, default, CancellationToken.None);
                if (previous.View.Id == "rbp")
                    await runtime.SetLiveAlgorithmEnabledAsync("rbp-topomap", false, default, CancellationToken.None);
                if (disposed || !IsSessionReady || restartGeneration != Interlocked.Read(ref generation) ||
                    runtime.LiveMontage?.Revision != montage.Revision) return;
                await ApplyAsync();
            }
            catch (Exception exception)
            {
                if (restartGeneration == Interlocked.Read(ref generation))
                    SetActive(null, "导联切换后算法重启失败");
                DesktopLog.Warning($"live_analysis_montage_restart_failed revision={montage.Revision} {exception.Message}");
            }
        });
    }

    private async Task ToggleAsync()
    {
        if (!IsInteractive) return;
        if (!IsOpen)
        {
            await OpenViewAsync(SelectedView.Id);
            return;
        }
        await ChangeAsync(null);
        IsOpen = false;
    }

    private async Task OpenViewAsync(string viewId)
    {
        if (!IsInteractive) return;
        Channels = runtime.LiveMontage is { } montage
            ? LiveSpectralPresentation.AnalysisChannelNames(montage)
            : runtime.StreamMetadata is { } stream
                ? LiveSpectralPresentation.AnalysisChannelNames(stream)
            : [];
        Channel = Channels.Contains(Channel) ? Channel : Channels.FirstOrDefault();
        SelectedView = Views.First(view => view.Id == viewId);
        IsOpen = true;
        await ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        if (!IsInteractive || !IsOpen || runtime.State.SessionId is not { } session ||
            runtime.StreamMetadata is not { } stream) return;
        if (Channels.Count == 0) throw new InvalidOperationException("当前采集会话没有可用的脑电通道。");

        var id = SelectedView.Id;
        var selectedChannel = NeedsChannel ? Channel ?? throw new InvalidOperationException("请选择分析通道。") : null;
        var mapField = id switch
        {
            "rbp-topomap" => $"rbp.{Power}.{Band}",
            "psd-topomap" => $"psd.band-mean-density.{Band}",
            _ => null,
        };
        var config = mapField is null
            ? LiveRbpPresentation.Config(selectedChannel is null ? Channels : [selectedChannel])
            : runtime.LiveMontage is { } montage
                ? LiveSpectralPresentation.MapConfig(montage, [mapField])
                : LiveSpectralPresentation.MapConfig(stream, [mapField]);
        var next = new Selection(session, SelectedView, selectedChannel, NeedsBand ? Band : null, mapField, config);
        if (active?.Config.GetRawText() == config.GetRawText() && active.View.Id == id) return;
        await ChangeAsync(next);
    }

    private async Task ChangeAsync(Selection? next)
    {
        isBusy = true;
        RaisePropertyChanged(nameof(IsInteractive));
        try
        {
            if (active is { } previous)
            {
                await runtime.SetLiveAlgorithmEnabledAsync(previous.View.Id, false, default, CancellationToken.None);
                if (previous.View.Id == "rbp")
                    await runtime.SetLiveAlgorithmEnabledAsync("rbp-topomap", false, default, CancellationToken.None);
            }

            SetActive(null, "选择图表");
            if (next is null || runtime.State.SessionId != next.Session || !IsSessionReady) return;

            // The bridge can publish the first cached window before enable returns.
            SetActive(next, "等待可用的 10 秒窗口");
            try
            {
                await runtime.SetLiveAlgorithmEnabledAsync(next.View.Id, true, next.Config, CancellationToken.None);
                if (next.View.Id == "rbp")
                {
                    var mapConfig = LiveSpectralPresentation.RbpTopomapConfig(
                        runtime.StreamMetadata!, runtime.LiveMontage, RbpTopomapFieldIds);
                    try
                    {
                        await runtime.SetLiveAlgorithmEnabledAsync("rbp-topomap", true, mapConfig, CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        DesktopLog.Warning($"live_rbp_topomap_start_failed {exception.Message}");
                    }
                }
            }
            catch
            {
                if (ReferenceEquals(active, next)) SetActive(null, "图表启动失败，可重新应用");
                throw;
            }
        }
        finally
        {
            isBusy = false;
            RaisePropertyChanged(nameof(IsInteractive));
        }
    }

    private void SetActive(Selection? selection, string message)
    {
        Interlocked.Increment(ref generation);
        active = selection;
        AnalysisStartSeconds = selection is null ? null : runtime.CurrentElapsedSeconds;
        revision = -1;
        topomapRevision = -1;
        Status = message;
        Window = "";
        Psd.Clear();
        Stft = null;
        Field = null;
        ColorScales.Reset();
        if (selection?.View.Id == "rbp") Rbp.Begin(selection.Session, AnalysisStartSeconds);
        else Rbp.Clear();
        RaisePropertyChanged(nameof(ActiveView));
        RaisePropertyChanged(nameof(ActiveLabel));
    }

    private void OnFault(object? sender, AcquisitionFault fault)
    {
        var observedGeneration = Interlocked.Read(ref generation);
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed || active is null || observedGeneration != Interlocked.Read(ref generation)) return;
            SetActive(null, fault.Detail);
        });
    }

    private void OnUpdate(object? sender, LiveAnalysisUpdate update)
    {
        var observedGeneration = Interlocked.Read(ref generation);
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed || observedGeneration != Interlocked.Read(ref generation) || active is not { } view ||
                runtime.State.SessionId != update.SessionId || view.Session != update.SessionId ||
                !IsSessionReady || update.Revision <= (update.AlgorithmId == "rbp-topomap" ? topomapRevision : revision) ||
                (runtime.LiveMontage is { } montage && !update.IsCurrentMontageResult(montage)) ||
                (view.View.Id != update.AlgorithmId && !(view.View.Id == "rbp" && update.AlgorithmId == "rbp-topomap"))) return;
            if (update.AlgorithmId == "rbp-topomap") topomapRevision = update.Revision;
            else revision = update.Revision;
            Window = update.StartSeconds is { } start && update.EndSeconds is { } end ? $"{start:0.###}–{end:0.###} s" : "";
            Status = update.State switch
            {
                "ready" => "",
                "partial" when view.View.Id is "rbp" or "rbp-topomap" => "",
                "partial" => "部分数据可用",
                "warming_up" => "等待可用的 10 秒窗口",
                _ => update.Message ?? "当前窗口不可用",
            };
            try
            {
                if (view.View.Id == "rbp")
                {
                    if (update.AlgorithmId == "rbp" && LiveRbpPresentation.Read(update, Channels) is { } rbp) Rbp.Apply(rbp);
                    else if (update.AlgorithmId == "rbp-topomap")
                    {
                        if (update.State is not ("ready" or "partial"))
                        {
                            Rbp.ClearTopomap();
                            return;
                        }
                        var fields = LiveSpectralPresentation.ReadMaps(update, RbpTopomapFieldIds);
                        Rbp.ApplyTopomap(fields, update.StartSeconds, update.EndSeconds);
                    }
                    return;
                }
                if (update.State is not ("ready" or "partial"))
                {
                    Psd.Clear();
                    Stft = null;
                    Field = null;
                    return;
                }
                if (update.StartSeconds is not { } left || update.EndSeconds is not { } right)
                    throw new InvalidOperationException("实时分析缺少窗口时间。");
                if (view.FieldId is { } mapField) Field = LiveSpectralPresentation.ReadMap(update, mapField);
                else if (view.View.Id == "stft") Stft = LiveSpectralPresentation.ReadStft(update, view.Channel!, new TimeRange(left, right));
                else LiveSpectralPresentation.ApplyPsd(Psd, update, view.Channel!);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                Psd.Clear();
                Stft = null;
                Field = null;
                Status = "实时分析结果格式不匹配";
                DesktopLog.Warning($"live_analysis_projection_invalid session={update.SessionId} algorithm={update.AlgorithmId} revision={update.Revision} {exception.Message}");
            }
        });
    }

    public void Dispose()
    {
        disposed = true;
        runtime.LiveAnalysisUpdated -= OnUpdate;
        runtime.LiveAnalysisFaulted -= OnFault;
        runtime.LiveMontageChanged -= OnMontageChanged;
    }

    private sealed record Selection(Guid Session, LiveSpectralOption View, string? Channel, string? Band, string? FieldId, JsonElement Config);

    private static readonly IReadOnlyList<string> RbpTopomapFieldIds =
        ["rbp.relative.delta", "rbp.relative.theta", "rbp.relative.alpha", "rbp.relative.beta", "rbp.relative.gamma",
         "rbp.absolute.delta", "rbp.absolute.theta", "rbp.absolute.alpha", "rbp.absolute.beta", "rbp.absolute.gamma"];
}
