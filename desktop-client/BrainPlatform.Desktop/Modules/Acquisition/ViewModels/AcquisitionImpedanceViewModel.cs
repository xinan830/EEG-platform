using System.Collections.ObjectModel;
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
    private readonly Dictionary<ImpedanceChannelIdentity, ImpedanceElectrodeRow> rowsByIdentity = [];
    private long lastCommunicationTick = Environment.TickCount64;
    private double colorScaleMaximumKiloOhms = 20;
    private string lowColor = "#16A34A";
    private string middleColor = "#FACC15";
    private string highColor = "#DC2626";
    private ImpedanceColorScale? colorScale = ImpedanceColorScale.Create(0, 10, 20);
    private ImpedanceElectrodeRow? selected;
    private bool disposed;

    public AcquisitionImpedanceViewModel(ConfiguredAcquisitionRuntime runtime, ChannelConfigurationProfile physical, Guid sessionId, Dispatcher dispatcher)
    {
        this.runtime = runtime;
        this.physical = physical;
        this.sessionId = sessionId;
        this.dispatcher = dispatcher;
        runtime.ImpedanceUpdated += OnUpdated;
        runtime.DeviceModeChanged += OnModeChanged;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshPresentation(rebuildScale: false), dispatcher);
        timer.Start();
    }

    public ObservableCollection<ImpedanceElectrodeRow> Rows { get; } = [];

    public double ColorScaleMaximumKiloOhms
    {
        get => colorScaleMaximumKiloOhms;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(value, 0, 200);
            if (SetProperty(ref colorScaleMaximumKiloOhms, clamped))
            {
                RaisePropertyChanged(nameof(ColorScaleMaximumText));
                RefreshPresentation(rebuildScale: true);
            }
        }
    }

    public string ColorScaleMaximumText => $"{ColorScaleMaximumKiloOhms:0} kΩ";
    public int TotalCount => Rows.Count;
    public int PassedCount => Rows.Count(row => row.IsWithinRange);
    public string SummaryText => TotalCount == 0
        ? "等待设备上报阻抗..."
        : $"范围内 {PassedCount} / {TotalCount}";

    public string LowColor
    {
        get => lowColor;
        set { if (SetProperty(ref lowColor, value)) RefreshPresentation(rebuildScale: true); }
    }

    public string MiddleColor
    {
        get => middleColor;
        set { if (SetProperty(ref middleColor, value)) RefreshPresentation(rebuildScale: true); }
    }

    public string HighColor
    {
        get => highColor;
        set { if (SetProperty(ref highColor, value)) RefreshPresentation(rebuildScale: true); }
    }

    public ImpedanceElectrodeRow? SelectedRow { get => selected; set => SetProperty(ref selected, value); }
    public ImpedanceColorScale? ColorScale { get => colorScale; private set => SetProperty(ref colorScale, value); }

    private void OnUpdated(object? sender, ImpedanceSnapshot snapshot)
    {
        if (snapshot.DetectionSessionId != sessionId) return;
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed || runtime.DeviceMode.DetectionSessionId != sessionId || runtime.DeviceMode.Mode != AcquisitionDeviceMode.Impedance) return;
            lastCommunicationTick = Environment.TickCount64;
            foreach (var reading in snapshot.Readings)
            {
                var identity = new ImpedanceChannelIdentity(reading.NativeChannelIndex, reading.Kind);
                if (!rowsByIdentity.TryGetValue(identity, out var row))
                {
                    row = ImpedanceElectrodeRow.Create(reading, physical);
                    rowsByIdentity[identity] = row;
                    Rows.Add(row);
                }
                row.Update(reading);
            }
            SelectedRow ??= Rows.FirstOrDefault();
            RefreshPresentation(rebuildScale: false);
        });
    }

    private void OnModeChanged(object? sender, AcquisitionDeviceModeSnapshot mode) =>
        _ = dispatcher.InvokeAsync(() => { if (!disposed) RefreshPresentation(rebuildScale: false); });

    private void RefreshPresentation(bool rebuildScale)
    {
        if (disposed) return;
        var stale = Environment.TickCount64 - lastCommunicationTick > 3000 || runtime.DeviceMode.Mode != AcquisitionDeviceMode.Impedance;
        if (rebuildScale)
        {
            ColorScale = ImpedanceColorScale.Create(0, ColorScaleMaximumKiloOhms / 2,
                ColorScaleMaximumKiloOhms, LowColor, MiddleColor, HighColor);
        }
        foreach (var row in Rows) row.SetPresentation(ColorScale, stale);
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(PassedCount));
        RaisePropertyChanged(nameof(SummaryText));
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        runtime.ImpedanceUpdated -= OnUpdated;
        runtime.DeviceModeChanged -= OnModeChanged;
    }
}
