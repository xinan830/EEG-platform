using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed record LiveSpectralOption(string Id, string Label);

/// <summary>Owns the temporary analysis panel and its one active view for the capture session.</summary>
public sealed class LiveSpectralAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly Dispatcher dispatcher;
    private Selection? active;
    private long generation;
    private long revision = -1;
    private bool disposed;
    private bool isOpen;
    private bool isBusy;
    private string status = "选择图表后点击应用";
    private string window = "";
    private StftPreview? stft;
    private TopomapField? topomapField;
    private string? channel;
    private IReadOnlyList<string> channels = [];
    private LiveSpectralOption selectedView = Views[0];

    public LiveSpectralAnalysisViewModel(ConfiguredAcquisitionRuntime runtime, Dispatcher dispatcher, Action<Exception> reportError)
    {
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, reportError);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, reportError);
        runtime.LiveAnalysisUpdated += OnUpdate;
        runtime.LiveAnalysisFaulted += OnFault;
    }

    public static IReadOnlyList<LiveSpectralOption> Views { get; } =
        [new("rbp", "RBP 趋势"), new("psd", "功率谱密度"), new("stft", "时频图"),
         new("rbp-topomap", "RBP 地形图"), new("psd-topomap", "PSD 地形图")];
    public IReadOnlyList<LiveSpectralOption> ViewOptions => Views;
    public IReadOnlyList<string> Bands { get; } = ["delta", "theta", "alpha", "beta", "gamma"];
    public IReadOnlyList<LiveSpectralOption> PowerOptions { get; } =
        [new("relative", "相对功率"), new("absolute", "绝对功率"), new("amplitude", "RMS 振幅")];
    public IReadOnlyList<string> Channels { get => channels; private set => SetProperty(ref channels, value); }
    public string? Channel { get => channel; set => SetProperty(ref channel, value); }
    public string Band { get; set; } = "alpha";
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
    public StftPreview? Stft { get => stft; private set => SetProperty(ref stft, value); }
    public TopomapField? Field { get => topomapField; private set => SetProperty(ref topomapField, value); }
    public PsdPreviewState Psd { get; } = new() { ValueUnit = "µV²/Hz" };
    public LiveRbpAnalysisViewModel Rbp { get; } = new();
    public TopomapColorScaleState ColorScales { get; } = new();
    public ICommand ToggleCommand { get; }
    public ICommand ApplyCommand { get; }

    public void RefreshSession()
    {
        if (!IsSessionReady || (active is { } current && runtime.State.SessionId != current.Session))
        {
            IsOpen = false;
            SetActive(null, "选择图表后点击应用");
        }
        RaisePropertyChanged(nameof(IsInteractive));
    }

    private async Task ToggleAsync()
    {
        if (!IsInteractive) return;
        if (!IsOpen)
        {
            Channels = runtime.StreamMetadata?.Channels
                .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
                .Select(LiveSpectralPresentation.ChannelName).ToArray() ?? [];
            Channel = Channels.Contains(Channel) ? Channel : Channels.FirstOrDefault();
            IsOpen = true;
            if (SelectedView.Id == "rbp" && Channels.Count > 0) await ApplyAsync();
            return;
        }
        await ChangeAsync(null);
        IsOpen = false;
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
            : LiveSpectralPresentation.MapConfig(stream, mapField);
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
                await runtime.SetLiveAlgorithmEnabledAsync(previous.View.Id, false, default, CancellationToken.None);

            SetActive(null, "选择图表后点击应用");
            if (next is null || runtime.State.SessionId != next.Session || !IsSessionReady) return;

            // The bridge can publish the first cached window before enable returns.
            SetActive(next, "等待可用的 10 秒窗口");
            try
            {
                await runtime.SetLiveAlgorithmEnabledAsync(next.View.Id, true, next.Config, CancellationToken.None);
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
        revision = -1;
        Status = message;
        Window = "";
        Psd.Clear();
        Stft = null;
        Field = null;
        ColorScales.Reset();
        if (selection?.View.Id == "rbp") Rbp.Begin(selection.Session);
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
                view.View.Id != update.AlgorithmId || !IsSessionReady || update.Revision <= revision) return;
            revision = update.Revision;
            Window = update.StartSeconds is { } start && update.EndSeconds is { } end ? $"{start:0.###}–{end:0.###} s" : "";
            Status = update.State switch
            {
                "ready" => "已更新",
                "partial" => "部分数据可用",
                "warming_up" => "等待可用的 10 秒窗口",
                _ => update.Message ?? "当前窗口不可用",
            };
            try
            {
                if (view.View.Id == "rbp")
                {
                    if (LiveRbpPresentation.Read(update, Channels) is { } rbp) Rbp.Apply(rbp);
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
    }

    private sealed record Selection(Guid Session, LiveSpectralOption View, string? Channel, string? Band, string? FieldId, JsonElement Config);
}
