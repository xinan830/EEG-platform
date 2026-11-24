using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

/// <summary>Owns the diagram-only draft positions for physical REF and GND leads.</summary>
public sealed class ElectrodeMapLandmarkDraft : ObservableObject
{
    private ElectrodeMapPosition referencePosition = ElectrodeMapDefaults.Reference;
    private ElectrodeMapPosition groundPosition = ElectrodeMapDefaults.Ground;

    public ElectrodeMapPosition ReferencePosition
    {
        get => referencePosition;
        set => SetProperty(ref referencePosition, ValidatePosition(value));
    }

    public ElectrodeMapPosition GroundPosition
    {
        get => groundPosition;
        set => SetProperty(ref groundPosition, ValidatePosition(value));
    }

    public void Load(ChannelConfigurationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ReferencePosition = profile.ReferenceMapPosition ?? ElectrodeMapDefaults.Reference;
        GroundPosition = profile.GroundMapPosition ?? ElectrodeMapDefaults.Ground;
    }

    public void Reset()
    {
        ReferencePosition = ElectrodeMapDefaults.Reference;
        GroundPosition = ElectrodeMapDefaults.Ground;
    }

    private static ElectrodeMapPosition ValidatePosition(ElectrodeMapPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
            position.X is < -1 or > 1 || position.Y is < -1 or > 1 ||
            position.X * position.X + position.Y * position.Y > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "硬件标记位置必须在头部圆形范围内。");
        }
        return position;
    }
}
