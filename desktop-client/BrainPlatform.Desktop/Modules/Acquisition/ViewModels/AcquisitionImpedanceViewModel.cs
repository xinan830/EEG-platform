using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Projects only the current detection session onto the UI Dispatcher.</summary>
public sealed class AcquisitionImpedanceViewModel : ObservableObject, IDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly ChannelConfigurationProfile physical;
    private readonly Guid sessionId;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private long lastCommunicationTick = Environment.TickCount64;
    private string statusText = "正在打开阻抗检测…";
    private string lastReceivedText = "尚未收到读数";
    private string lowerText = string.Empty;
    private string upperText = string.Empty;
    private ImpedanceElectrodeRow? selected;
    private bool disposed;
    public AcquisitionImpedanceViewModel(ConfiguredAcquisitionRuntime runtime, ChannelConfigurationProfile physical, Guid sessionId, Dispatcher dispatcher)
    {
        this.runtime = runtime;
        this.physical = physical;
        this.sessionId = sessionId;
        this.dispatcher = dispatcher;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.SortDescriptions.Add(new(nameof(ImpedanceElectrodeRow.SortResistance), ListSortDirection.Descending));
        runtime.ImpedanceUpdated += OnUpdated;
        runtime.DeviceModeChanged += OnModeChanged;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshPresentation(), dispatcher);
        timer.Start();
    }
    public ObservableCollection<ImpedanceElectrodeRow> Rows { get; } = [];
    public ICollectionView RowsView { get; }
    public string DeviceText => physical.DeviceDisplayName;
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string LastReceivedText { get => lastReceivedText; private set => SetProperty(ref lastReceivedText, value); }
    public ImpedanceElectrodeRow? SelectedRow { get => selected; set => SetProperty(ref selected, value); }
    public string LowerText { get => lowerText; set { if (SetProperty(ref lowerText, value)) RefreshPresentation(); } }
    public string UpperText { get => upperText; set { if (SetProperty(ref upperText, value)) RefreshPresentation(); } }
    public string LegendText => TryThresholds(out var first, out var second)
        ? $"绿 ≤ {first:g} · 黄 ({first:g}, {second:g}] · 红 > {second:g} kΩ"
        : "请设置操作阈值：0 < T1 < T2（kΩ）；未设置时以中性色显示。";
    public string HardwareStatusText => string.Join(" · ", new[]
    {
        Rows.Any(row => row.Identity.Kind == AcquisitionChannelKind.ImpedanceReference) ? "REF：见列表" : "REF：SDK 未提供测量",
        Rows.Any(row => row.Identity.Kind == AcquisitionChannelKind.ImpedanceGround) ? "GND：见列表" : "GND：SDK 未提供测量",
    });

    private bool TryThresholds(out double first, out double second)
    {
        var validFirst = double.TryParse(lowerText, NumberStyles.Float, CultureInfo.CurrentCulture, out first);
        var validSecond = double.TryParse(upperText, NumberStyles.Float, CultureInfo.CurrentCulture, out second);
        return validFirst && validSecond && double.IsFinite(first) && double.IsFinite(second) && first > 0 && second > first;
    }
    private void OnUpdated(object? sender, ImpedanceSnapshot snapshot)
    {
        if (snapshot.DetectionSessionId != sessionId) return;
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed || runtime.DeviceMode.DetectionSessionId != sessionId || runtime.DeviceMode.Mode != AcquisitionDeviceMode.Impedance) return;
            lastCommunicationTick = Environment.TickCount64;
            LastReceivedText = $"接收时间 {snapshot.ReceivedAtUtc.ToLocalTime():HH:mm:ss.fff} · kΩ";
            foreach (var reading in snapshot.Readings)
            {
                var row = Rows.SingleOrDefault(item => item.Identity == new ImpedanceChannelIdentity(reading.NativeChannelIndex, reading.Kind));
                if (row is null) { row = ImpedanceElectrodeRow.Create(reading, physical); Rows.Add(row); }
                row.Update(reading);
            }
            SelectedRow ??= Rows.FirstOrDefault();
            RaisePropertyChanged(nameof(HardwareStatusText));
            RefreshPresentation();
        });
    }
    private void OnModeChanged(object? sender, AcquisitionDeviceModeSnapshot mode) =>
        _ = dispatcher.InvokeAsync(() => { if (!disposed) RefreshPresentation(); });
    private void RefreshPresentation()
    {
        if (disposed) return;
        var stale = Environment.TickCount64 - lastCommunicationTick > 3000 || runtime.DeviceMode.Mode != AcquisitionDeviceMode.Impedance;
        var hasThresholds = TryThresholds(out var first, out var second);
        foreach (var row in Rows) row.SetPresentation(hasThresholds ? first : null, hasThresholds ? second : null, stale);
        StatusText = runtime.DeviceMode.Mode == AcquisitionDeviceMode.Impedance && stale
            ? "超过 3 秒未收到测量状态；当前数值已过期。" : runtime.DeviceMode.Detail;
        var selectedBeforeRefresh = SelectedRow;
        RowsView.Refresh();
        SelectedRow = selectedBeforeRefresh;
        RaisePropertyChanged(nameof(LegendText));
    }
    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        runtime.ImpedanceUpdated -= OnUpdated;
        runtime.DeviceModeChanged -= OnModeChanged;
    }
}
