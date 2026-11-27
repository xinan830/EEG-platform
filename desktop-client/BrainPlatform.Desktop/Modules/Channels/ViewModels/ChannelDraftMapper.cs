using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

/// <summary>Stateless conversion between device capabilities, draft rows, and saved channel profiles.</summary>
internal static class ChannelDraftMapper
{
    public static bool IsEeg(AcquisitionChannelCapability capability) =>
        capability.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;

    public static string DefaultLocation(string? location, string fallback) =>
        string.IsNullOrWhiteSpace(location) ? fallback : location.Trim();

    public static AcquisitionDeviceDescriptor BuildDeviceDescriptor(ChannelMappingViewModel mapping)
    {
        var current = mapping.AllRows.Select(row => new AcquisitionChannelCapability(
            row.NativeChannelIndex, Enum.Parse<AcquisitionChannelKind>(row.Role), row.Unit)).ToArray();
        return new AcquisitionDeviceDescriptor(
            mapping.CurrentDeviceKey ?? string.Empty,
            mapping.CurrentDeviceDisplayName ?? string.Empty,
            null, [],
            ChannelCapabilities: current,
            Model: mapping.CurrentDeviceModel,
            DriverId: mapping.CurrentDeviceDriverId);
    }

    public static IReadOnlyList<ChannelConfigurationDraftRow> BuildRows(
        AcquisitionDeviceDescriptor device,
        ChannelConfigurationProfile? profile)
    {
        var capabilities = device.ChannelCapabilities ?? [];
        var eeg = capabilities.Where(IsEeg).ToArray();
        var hasDefault = DefaultChannelLabelTemplate.TryGetFor(eeg, out var defaults);
        var entries = profile?.Channels.ToDictionary(entry => entry.NativeChannelIndex) ?? [];
        var rows = new List<ChannelConfigurationDraftRow>(eeg.Length);
        foreach (var capability in capabilities.Where(IsEeg).OrderBy(item => item.NativeChannelIndex))
        {
            entries.TryGetValue(capability.NativeChannelIndex, out var entry);
            var isEeg = IsEeg(capability);
            var label = entry?.ElectrodeLabel
                ?? (hasDefault ? defaults.GetValueOrDefault(capability.NativeChannelIndex) ?? string.Empty : string.Empty);
            var enabled = entry?.IsSelectedForDisplay ?? (isEeg && !string.IsNullOrWhiteSpace(label));
            var row = new ChannelConfigurationDraftRow(
                capability.NativeChannelIndex, capability.Kind.ToString(), capability.Unit,
                isEeg ? "µV" : capability.Unit, label, enabled,
                entry?.DisplayOrder ?? capability.NativeChannelIndex, isEeg, entry?.ElectrodeLocation);
            row.SetManualMapPosition(entry?.ManualMapPosition);
            rows.Add(row);
        }
        return rows;
    }

    public static ChannelConfigurationProfile CreateProfileSnapshot(
        AcquisitionDeviceDescriptor device,
        IReadOnlyCollection<ChannelConfigurationDraftRow> draftRows,
        string? draftProfileId,
        string draftName,
        string draftDescription,
        DateTimeOffset? draftCreatedAtUtc,
        DateTimeOffset updatedAt,
        string draftReferenceLocation,
        string draftGroundLocation,
        ElectrodeMapLandmarkDraft landmarks)
    {
        var entries = draftRows.Where(row => row.IsEegInput).Select(row => new ChannelConfigurationEntry(
            row.NativeChannelIndex, Enum.Parse<AcquisitionChannelKind>(row.Role),
            row.ElectrodeLabel.Trim(), row.IsEnabled, row.DisplayOrder,
            row.ElectrodeLocation, row.ManualMapPosition)).ToArray();
        return new ChannelConfigurationProfile(
            draftProfileId ?? Guid.NewGuid().ToString("N"),
            draftName.Trim(),
            draftDescription.Trim(),
            ChannelConfigurationSource.User,
            AcquisitionDeviceCompatibility.CreateSignature(device.DriverId, device.ChannelCapabilities),
            entries,
            draftCreatedAtUtc ?? updatedAt,
            updatedAt,
            device.DisplayName,
            AcquisitionDeviceCompatibility.NormalizeDriverId(device.DriverId),
            device.Model ?? string.Empty,
            draftReferenceLocation.Trim(),
            draftGroundLocation.Trim(),
            landmarks.ReferencePosition,
            landmarks.GroundPosition);
    }

    public static ChannelConfigurationProfile CreateSavedBaseline(
        ChannelConfigurationProfile profile,
        ChannelConfigurationProfile currentSnapshot) =>
        currentSnapshot with
        {
            Name = profile.Name,
            Description = profile.Description,
            DeviceSignature = profile.DeviceSignature,
            HardwareReferenceElectrodeLocation = DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF"),
            HardwareGroundElectrodeLocation = DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND"),
            ReferenceMapPosition = profile.ReferenceMapPosition ?? ElectrodeMapDefaults.Reference,
            GroundMapPosition = profile.GroundMapPosition ?? ElectrodeMapDefaults.Ground,
            Channels = profile.Channels,
        };

    public static bool IsEquivalent(ChannelConfigurationProfile current, ChannelConfigurationProfile baseline) =>
        current.Name == baseline.Name &&
        current.Description == baseline.Description &&
        current.DeviceSignature == baseline.DeviceSignature &&
        current.HardwareReferenceElectrodeLocation == baseline.HardwareReferenceElectrodeLocation &&
        current.HardwareGroundElectrodeLocation == baseline.HardwareGroundElectrodeLocation &&
        current.ReferenceMapPosition == (baseline.ReferenceMapPosition ?? ElectrodeMapDefaults.Reference) &&
        current.GroundMapPosition == (baseline.GroundMapPosition ?? ElectrodeMapDefaults.Ground) &&
        current.Channels.SequenceEqual(baseline.Channels);
}
