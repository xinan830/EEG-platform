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
        var returned = sdkChannels.Select(channel => new ImpedanceChannelIdentity(
            channel.Index,
            AntEegoChannelMapper.Map(0, channel.Index, channel.Type).Kind)).ToHashSet();
        var physicalInputs = returned.Where(channel => channel.Kind == AcquisitionChannelKind.Reference).ToHashSet();
        if (returned.Count != sdkChannels.Count || !physicalInputs.SetEquals(requestedChannels) ||
            returned.Any(channel => channel.Kind is not (AcquisitionChannelKind.Reference or
                AcquisitionChannelKind.ImpedanceReference or AcquisitionChannelKind.ImpedanceGround)))
        {
            throw new AntEegoSdkException(
                "ANT_EEGO_IMPEDANCE_CHANNEL_IDENTITY_MISMATCH",
                "The SDK opened impedance channels that do not match the requested physical electrode identities.");
        }

        var metadata = new ImpedanceStreamMetadata(
            deviceId,
            deviceName,
            sdkChannels.Select((channel, streamIndex) => new ImpedanceChannel(
                streamIndex,
                channel.Index,
                AntEegoChannelMapper.Map(0, channel.Index, channel.Type).Kind,
                channel.Type switch
                {
                    AntEegoNativeChannelType.ImpedanceReference => "REF",
                    AntEegoNativeChannelType.ImpedanceGround => "GND",
                    _ => labelsByNativeIndex?.GetValueOrDefault(channel.Index),
                })).ToArray());
        metadata.Validate();
        return metadata;
    }
}
