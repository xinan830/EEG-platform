using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>One physical SDK channel, displayed in kΩ without changing the measurement.</summary>
public sealed class ImpedanceElectrodeRow : ObservableObject
{
    private double? resistanceOhms;
    private ImpedanceReadingStatus status = ImpedanceReadingStatus.Missing;
    private bool stale;
    private double? lower;
    private double? upper;
    public ImpedanceElectrodeRow(ImpedanceChannelIdentity identity, string label, ElectrodeMapPosition? position)
    {
        Identity = identity;
        Label = label;
        Position = position;
    }
    public ImpedanceChannelIdentity Identity { get; }
    public string Label { get; }
    public ElectrodeMapPosition? Position { get; }
    public string NativeIdentityText => $"{Identity.Kind} · #{Identity.NativeChannelIndex}";
    public double SortResistance => status == ImpedanceReadingStatus.Valid ? resistanceOhms ?? double.MaxValue : double.MaxValue;
    public string ValueText => resistanceOhms is { } value ? (value / 1000d).ToString("0.0", CultureInfo.CurrentCulture) : "—";
    public string StatusText => stale ? "通信超时，数值已过期" : status switch
    {
        ImpedanceReadingStatus.Missing => "等待测量",
        ImpedanceReadingStatus.Invalid => "SDK 返回无效数值",
        _ when lower is null => "未设置操作阈值",
        _ when resistanceOhms / 1000d <= lower => "低于第一阈值",
        _ when resistanceOhms / 1000d <= upper => "介于两阈值之间",
        _ => "高于第二阈值",
    };
    public string Color => stale || status != ImpedanceReadingStatus.Valid ? "#94A3B8" :
        ThresholdColor(resistanceOhms!.Value / 1000d, lower, upper);
    public string MapText => $"{Label}\n{ValueText}";
    public string AccessibleText => $"{Label}，{ValueText} kΩ，{StatusText}，{NativeIdentityText}";

    public void Update(ImpedanceReading reading)
    {
        resistanceOhms = reading.ResistanceOhms;
        status = reading.Status;
        stale = false;
        Notify();
    }
    public void SetPresentation(double? first, double? second, bool isStale)
    {
        lower = first;
        upper = second;
        stale = isStale;
        Notify();
    }
    private void Notify()
    {
        foreach (var property in new[] { nameof(ValueText), nameof(StatusText), nameof(Color),
            nameof(MapText), nameof(AccessibleText), nameof(SortResistance) }) RaisePropertyChanged(property);
    }
    public static string ThresholdColor(double kilohms, double? first, double? second) =>
        first is not > 0 || second is null || !double.IsFinite(first.Value) || !double.IsFinite(second.Value) || first >= second
            ? "#64748B" : kilohms <= first ? "#16A34A" : kilohms <= second ? "#D97706" : "#DC2626";

    public static ImpedanceElectrodeRow Create(ImpedanceReading reading, ChannelConfigurationProfile physical)
    {
        var entry = physical.Channels.SingleOrDefault(channel => channel.NativeChannelIndex == reading.NativeChannelIndex &&
            channel.ExpectedKind == reading.Kind);
        var label = reading.Kind switch
        {
            AcquisitionChannelKind.ImpedanceReference => "REF",
            AcquisitionChannelKind.ImpedanceGround => "GND",
            _ => entry?.ElectrodeLabel ?? reading.Label ?? $"输入 {reading.NativeChannelIndex}",
        };
        var position = reading.Kind switch
        {
            AcquisitionChannelKind.ImpedanceReference => physical.ReferenceMapPosition,
            AcquisitionChannelKind.ImpedanceGround => physical.GroundMapPosition,
            _ => entry?.ManualMapPosition ?? ElectrodeMapProjection.ResolvePosition(label, entry?.ElectrodeLocation),
        };
        if (position is not null && (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
            position.X * position.X + position.Y * position.Y > 1)) position = null;
        return new(new(reading.NativeChannelIndex, reading.Kind), label, position);
    }
}
