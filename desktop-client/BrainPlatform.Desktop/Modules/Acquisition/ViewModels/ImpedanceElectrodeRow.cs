using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>One physical SDK channel, displayed in kΩ without changing the measurement.</summary>
public sealed class ImpedanceElectrodeRow : ObservableObject
{
    private double? resistanceOhms;
    private ImpedanceReadingStatus status = ImpedanceReadingStatus.Missing;
    private bool stale;
    private ImpedanceColorScale? scale;
    public ImpedanceElectrodeRow(ImpedanceChannelIdentity identity, string label, ElectrodeMapPosition? position)
    {
        Identity = identity;
        Label = label;
        Position = position;
    }
    public ImpedanceChannelIdentity Identity { get; }
    public string Label { get; }
    public ElectrodeMapPosition? Position { get; }
    public double? ResistanceKiloOhms => resistanceOhms is { } value ? value / 1000d : null;
    public bool IsWithinRange => !stale && status == ImpedanceReadingStatus.Valid && scale is not null &&
        ResistanceKiloOhms is { } k && k <= scale.Maximum;
    public string NativeIdentityText => $"{Identity.Kind} · #{Identity.NativeChannelIndex}";
    public string ValueText => resistanceOhms is { } value ? (value / 1000d).ToString("0.0", CultureInfo.CurrentCulture) : "—";
    public string StatusText => stale ? "通信超时，数值已过期" : status switch
    {
        ImpedanceReadingStatus.Missing => "等待测量",
        ImpedanceReadingStatus.Invalid => "SDK 返回无效数值",
        _ when scale is null => "未设置有效色标",
        _ when resistanceOhms / 1000d < scale.Minimum => "低于色标下限",
        _ when resistanceOhms / 1000d > scale.Maximum => "高于色标上限",
        _ => "色标范围内",
    };
    public string Color => stale || status != ImpedanceReadingStatus.Valid ? "#94A3B8" :
        scale?.ColorAt(resistanceOhms!.Value / 1000d) ?? "#64748B";
    public string AccessibleText => $"{Label}，{ValueText} kΩ，{StatusText}，{NativeIdentityText}";

    public void Update(ImpedanceReading reading)
    {
        resistanceOhms = reading.ResistanceOhms;
        status = reading.Status;
        stale = false;
        Notify();
    }
    public void SetPresentation(ImpedanceColorScale? colorScale, bool isStale)
    {
        if (ReferenceEquals(scale, colorScale) && stale == isStale) return;
        scale = colorScale;
        stale = isStale;
        Notify();
    }
    private void Notify()
    {
        foreach (var property in new[] { nameof(ValueText), nameof(StatusText), nameof(Color),
            nameof(AccessibleText) }) RaisePropertyChanged(property);
    }

    public static ImpedanceElectrodeRow Create(ImpedanceReading reading, ChannelConfigurationProfile physical)
    {
        var entry = physical.Channels.FirstOrDefault(channel => channel.NativeChannelIndex == reading.NativeChannelIndex &&
            channel.ExpectedKind == reading.Kind);
        var label = reading.Kind switch
        {
            AcquisitionChannelKind.ImpedanceReference => "REF",
            AcquisitionChannelKind.ImpedanceGround => "GND",
            _ => entry?.ElectrodeLabel ?? reading.Label ?? $"输入 {reading.NativeChannelIndex}",
        };
        var position = reading.Kind switch
        {
            AcquisitionChannelKind.ImpedanceReference => physical.ReferenceMapPosition is { } reference
                ? ElectrodeMapDefaults.ReferenceForDisplay(reference) : null,
            AcquisitionChannelKind.ImpedanceGround => physical.GroundMapPosition,
            _ => entry?.ManualMapPosition ?? ElectrodeMapProjection.ResolvePosition(label, entry?.ElectrodeLocation),
        };
        if (position is not null && (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
            position.X * position.X + position.Y * position.Y > 1)) position = null;
        return new(new(reading.NativeChannelIndex, reading.Kind), label, position);
    }
}
