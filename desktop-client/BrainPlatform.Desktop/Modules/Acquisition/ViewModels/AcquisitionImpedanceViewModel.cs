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
    private string minimumText = "0";
    private string middleText = string.Empty;
    private string maximumText = string.Empty;
    private string lowColor = "#16A34A";
    private string middleColor = "#FACC15";
    private string highColor = "#DC2626";
    private ImpedanceColorScale? colorScale;
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
    public string MinimumText { get => minimumText; set { if (SetProperty(ref minimumText, value)) RefreshPresentation(); } }
    public string MiddleText { get => middleText; set { if (SetProperty(ref middleText, value)) RefreshPresentation(); } }
    public string MaximumText { get => maximumText; set { if (SetProperty(ref maximumText, value)) RefreshPresentation(); } }
    public string LowColor { get => lowColor; set { if (SetProperty(ref lowColor, value)) RefreshPresentation(); } }
    public string MiddleColor { get => middleColor; set { if (SetProperty(ref middleColor, value)) RefreshPresentation(); } }
    public string HighColor { get => highColor; set { if (SetProperty(ref highColor, value)) RefreshPresentation(); } }
    public ImpedanceColorScale? ColorScale { get => colorScale; private set => SetProperty(ref colorScale, value); }
    public string LegendText => ColorScale is { } scale
        ? $"{scale.Minimum:g} → {scale.Middle:g} → {scale.Maximum:g} kΩ · 连续渐变，超出范围使用端点颜色"
        : "设置色标：0 ≤ 最低 < 中间 < 最高（kΩ），颜色须为有效 #RRGGBB；未设置时显示中性色。";
    public string HardwareStatusText => string.Join(" · ", new[]
    {
        Rows.Any(row => row.Identity.Kind == AcquisitionChannelKind.ImpedanceReference) ? "REF：见列表" : "REF：SDK 未提供测量",
        Rows.Any(row => row.Identity.Kind == AcquisitionChannelKind.ImpedanceGround) ? "GND：见列表" : "GND：SDK 未提供测量",
    });

    private ImpedanceColorScale? ParseColorScale()
    {
        return double.TryParse(minimumText, NumberStyles.Float, CultureInfo.CurrentCulture, out var minimum) &&
            double.TryParse(middleText, NumberStyles.Float, CultureInfo.CurrentCulture, out var middle) &&
            double.TryParse(maximumText, NumberStyles.Float, CultureInfo.CurrentCulture, out var maximum)
                ? ImpedanceColorScale.Create(minimum, middle, maximum, lowColor, middleColor, highColor) : null;
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
        ColorScale = ParseColorScale();
        foreach (var row in Rows) row.SetPresentation(ColorScale, stale);
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
