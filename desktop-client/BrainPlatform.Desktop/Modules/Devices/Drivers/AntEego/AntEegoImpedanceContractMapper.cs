namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

internal static class AntEegoImpedanceContractMapper
{
    public static AntEegoNativeChannelInfo[] ToNativeRequest(IReadOnlyList<ImpedanceChannelIdentity> requestedChannels) =>
        requestedChannels.Select(AntEegoChannelMapper.ToNativeImpedanceChannel).ToArray();

    public static ImpedanceStreamMetadata CreateMetadata(
        string deviceId,
        string deviceName,
        IReadOnlyList<ImpedanceChannelIdentity> requestedChannels,
        IReadOnlyList<AntEegoNativeChannelInfo> sdkChannels,
        IReadOnlyDictionary<int, string>? labelsByNativeIndex)
    {
        var physicalInputCount = sdkChannels.Count(channel => channel.Type == AntEegoNativeChannelType.Reference);
        var invalid = sdkChannels.Where(channel => channel.Type is not (AntEegoNativeChannelType.Reference or
            AntEegoNativeChannelType.ImpedanceReference or AntEegoNativeChannelType.ImpedanceGround)).ToArray();
        var duplicateCount = sdkChannels.Count - sdkChannels.Select(channel => (channel.Index, channel.Type)).Distinct().Count();
        if (duplicateCount > 0 || physicalInputCount != requestedChannels.Count || invalid.Length > 0)
        {
            throw new AntEegoSdkException(
                "ANT_EEGO_IMPEDANCE_CHANNEL_IDENTITY_MISMATCH",
                $"Impedance channel identity mismatch; requested=[{string.Join(",", requestedChannels.Select(Format))}], " +
                $"SDK=[{string.Join(",", sdkChannels.Select(channel => $"{channel.Type}:{channel.Index}"))}], " +
                $"requested_physical_count={requestedChannels.Count}, sdk_physical_count={physicalInputCount}, " +
                $"invalid=[{string.Join(",", invalid.Select(channel => $"{channel.Type}:{channel.Index}"))}], " +
                $"duplicate_count={duplicateCount}.");
        }

        var physicalInputIndex = 0;
        var metadata = new ImpedanceStreamMetadata(
            deviceId,
            deviceName,
            sdkChannels.Select((channel, streamIndex) =>
            {
                var kind = AntEegoChannelMapper.Map(0, channel.Index, channel.Type).Kind;
                var identity = kind == AcquisitionChannelKind.Reference
                    ? requestedChannels[physicalInputIndex++]
                    : new ImpedanceChannelIdentity(channel.Index, kind);
                return new ImpedanceChannel(
                    streamIndex,
                    identity.NativeChannelIndex,
                    kind,
                    kind switch
                    {
                        AcquisitionChannelKind.ImpedanceReference => "REF",
                        AcquisitionChannelKind.ImpedanceGround => "GND",
                        _ => labelsByNativeIndex?.GetValueOrDefault(identity.NativeChannelIndex),
                    });
            }).ToArray());
        metadata.Validate();
        return metadata;
    }

    private static string Format(ImpedanceChannelIdentity channel) => $"{channel.Kind}:{channel.NativeChannelIndex}";
}
