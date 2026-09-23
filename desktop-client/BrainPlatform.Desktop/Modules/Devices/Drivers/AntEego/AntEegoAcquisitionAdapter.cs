using System.IO;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

/// <summary>
/// Optional ANT/eego C ABI adapter. It is not the desktop default and does not
/// infer electrode labels or acquisition ranges.
/// </summary>
public sealed class AntEegoAcquisitionAdapter : IAcquisitionDeviceAdapter, IDisposable
{
    private readonly AntEegoAdapterOptions options;
    private readonly object streamGate = new();
    private AntEegoStream? activeStream;
    private bool disposed;

    public AntEegoAcquisitionAdapter(AntEegoAdapterOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public DeviceReadiness GetReadiness()
    {
        if (string.IsNullOrWhiteSpace(options.SdkLibraryPath) || !File.Exists(options.SdkLibraryPath))
        {
            return new DeviceReadiness(false, "未配置", "未找到 ANT/eego eego-SDK.dll 路径");
        }

        return new DeviceReadiness(false, "待测试", "SDK 已选择；请测试放大器连接以读取实际设备能力");
    }

    public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        options.ValidateSdkPath();
        cancellationToken.ThrowIfCancellationRequested();

        using var lease = AntEegoNativeRuntime.Acquire(options.SdkLibraryPath, forStream: false);
        return Task.FromResult(lease.Invoke(api => DiscoverDevices(api)));
    }

    public Task<IAcquisitionStream> OpenEegStreamAsync(
        AcquisitionStreamRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        request.Validate();
        options.ValidateForStream();
        cancellationToken.ThrowIfCancellationRequested();

        lock (streamGate)
        {
            if (activeStream is not null)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_STREAM_ALREADY_ACTIVE",
                    "This ANT/eego adapter already owns an active EEG stream.");
            }
        }

        var identity = AntEegoDeviceIdentity.Parse(request.DeviceId);
        var lease = AntEegoNativeRuntime.Acquire(options.SdkLibraryPath, forStream: true);
        var amplifierOpened = false;
        var streamId = -1;
        try
        {
            var opened = lease.Invoke(api => OpenStream(api, identity, request));
            amplifierOpened = true;
            streamId = opened.StreamId;
            var stream = new AntEegoStream(
                lease,
                identity.NativeId,
                streamId,
                opened.Metadata,
                options.EffectiveEmptyReadDelay,
                OnStreamDisposed);
            lock (streamGate)
            {
                activeStream = stream;
            }

            return Task.FromResult<IAcquisitionStream>(stream);
        }
        catch
        {
            try
            {
                lease.Invoke(api =>
                {
                    if (streamId >= 0)
                    {
                        api.CloseStream(streamId);
                    }

                    if (amplifierOpened)
                    {
                        api.CloseAmplifier(identity.NativeId);
                    }
                });
            }
            catch
            {
                // The original opening error remains the actionable error.
            }
            finally
            {
                lease.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        lock (streamGate)
        {
            disposed = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<AcquisitionDeviceDescriptor> DiscoverDevices(AntEegoNativeApi api)
    {
        var descriptors = new List<AcquisitionDeviceDescriptor>();
        foreach (var amplifier in api.GetAmplifiers())
        {
            api.OpenAmplifier(amplifier.Id);
            try
            {
                var deviceMetadata = AntEegoDeviceMetadata.Create(
                    api.GetAmplifierType(amplifier.Id),
                    amplifier.Serial);
                var channelCapabilities = api.GetAmplifierChannels(amplifier.Id)
                    .Select(channel => AntEegoChannelMapper.Map(0, channel.Index, channel.Type))
                    .Select(channel => new AcquisitionChannelCapability(
                        channel.NativeChannelIndex,
                        channel.Kind,
                        channel.Unit))
                    .ToArray();
                descriptors.Add(new AcquisitionDeviceDescriptor(
                    new AntEegoDeviceIdentity(amplifier.Id).ToDeviceId(),
                    string.IsNullOrWhiteSpace(amplifier.Serial) ? "ANT/eego amplifier" : $"ANT/eego {amplifier.Serial}",
                    string.IsNullOrWhiteSpace(amplifier.Serial) ? null : amplifier.Serial,
                    api.GetSamplingRates(amplifier.Id),
                    api.GetReferenceRanges(amplifier.Id),
                    api.GetBipolarRanges(amplifier.Id),
                    channelCapabilities,
                    Model: deviceMetadata.Model,
                    DeviceInstanceId: deviceMetadata.DeviceInstanceId));
            }
            finally
            {
                api.CloseAmplifier(amplifier.Id);
            }
        }

        return descriptors;
    }

    private OpenedAntEegoStream OpenStream(
        AntEegoNativeApi api,
        AntEegoDeviceIdentity identity,
        AcquisitionStreamRequest request)
    {
        var discovered = api.GetAmplifiers();
        if (!discovered.Any(amplifier => amplifier.Id == identity.NativeId))
        {
            throw new AntEegoSdkException(
                "ANT_EEGO_DEVICE_NOT_FOUND",
                "The selected ANT/eego device is no longer present. Discover devices again.");
        }

        var amplifierOpened = false;
        var openedStreamId = -1;
        try
        {
            api.OpenAmplifier(identity.NativeId);
            amplifierOpened = true;
            var samplingRates = api.GetSamplingRates(identity.NativeId);
            if (!samplingRates.Contains(request.SamplingRateHz))
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_SAMPLING_RATE_UNAVAILABLE",
                    $"The selected device does not report {request.SamplingRateHz} Hz as an available sampling rate.");
            }

            var referenceRange = options.ReferenceRangeVolts!.Value;
            var bipolarRange = options.BipolarRangeVolts!.Value;
            if (!api.GetReferenceRanges(identity.NativeId).Contains(referenceRange) ||
                !api.GetBipolarRanges(identity.NativeId).Contains(bipolarRange))
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_RANGE_UNAVAILABLE",
                    "Configured reference or bipolar range is not reported by the selected device.");
            }

            var selectedChannels = api.GetAmplifierChannels(identity.NativeId).ToArray();
            if (selectedChannels.Length == 0)
            {
                throw new AntEegoSdkException("ANT_EEGO_CHANNEL_LIST_EMPTY", "The selected device returned no EEG stream channels.");
            }

            openedStreamId = api.OpenEegStream(
                identity.NativeId,
                request.SamplingRateHz,
                referenceRange,
                bipolarRange,
                selectedChannels);
            var actualChannels = api.GetStreamChannels(openedStreamId);
            var actualChannelCount = api.GetStreamChannelCount(openedStreamId);
            if (actualChannels.Count != actualChannelCount)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_STREAM_CHANNEL_MISMATCH",
                    "The opened stream channel list does not match its reported channel count.");
            }

            var channels = actualChannels
                .Select((channel, streamIndex) => AntEegoChannelMapper.Map(
                    streamIndex,
                    channel.Index,
                    channel.Type,
                    options.ChannelLabelsByNativeIndex))
                .ToArray();
            var counterChannels = channels.Where(channel => channel.Kind == AcquisitionChannelKind.SampleCounter).ToArray();
            if (counterChannels.Length != 1)
            {
                throw new AntEegoSdkException(
                    "ANT_EEGO_SAMPLE_COUNTER_REQUIRED",
                    "The opened stream must report exactly one sample-counter channel.");
            }

            var selectedAmplifier = discovered.Single(amplifier => amplifier.Id == identity.NativeId);
            var deviceMetadata = AntEegoDeviceMetadata.Create(
                api.GetAmplifierType(identity.NativeId),
                selectedAmplifier.Serial);
            var hardwareConfiguration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["vendor"] = "ANT/eego",
                ["model"] = deviceMetadata.Model ?? "not_provided_by_sdk",
                ["device_instance_id"] = deviceMetadata.DeviceInstanceId ?? "not_provided_by_sdk",
                ["serial_number"] = string.IsNullOrWhiteSpace(selectedAmplifier.Serial)
                    ? "not_provided_by_sdk"
                    : selectedAmplifier.Serial,
                ["sdk_version"] = AntEegoNativeApi.ExpectedSdkVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reference_range_v"] = referenceRange.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["bipolar_range_v"] = bipolarRange.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["channel_label_source"] = "not_provided_by_sdk",
                ["hardware_reference_role"] = "REF",
                ["hardware_reference_electrode_location"] = DescribeRecordedLocation(options.HardwareReferenceElectrodeLocation),
                ["hardware_ground_role"] = "GND",
                ["hardware_ground_electrode_location"] = DescribeRecordedLocation(options.HardwareGroundElectrodeLocation),
            };
            AddOptionalHardwareConfiguration(hardwareConfiguration, "channel_configuration_id", options.ChannelConfigurationId);
            AddOptionalHardwareConfiguration(hardwareConfiguration, "channel_configuration_name", options.ChannelConfigurationName);
            AddOptionalHardwareConfiguration(hardwareConfiguration, "channel_configuration_snapshot_json", options.ChannelConfigurationSnapshotJson);
            AddOptionalHardwareConfiguration(hardwareConfiguration, "montage_configuration_id", options.MontageConfigurationId);
            AddOptionalHardwareConfiguration(hardwareConfiguration, "montage_configuration_name", options.MontageConfigurationName);
            AddOptionalHardwareConfiguration(hardwareConfiguration, "montage_configuration_snapshot_json", options.MontageConfigurationSnapshotJson);

            var metadata = new AcquisitionStreamMetadata(
                identity.ToDeviceId(),
                string.IsNullOrWhiteSpace(selectedAmplifier.Serial) ? "ANT/eego amplifier" : $"ANT/eego {selectedAmplifier.Serial}",
                request.SamplingRateHz,
                channels,
                counterChannels[0].StreamIndex,
                DateTimeOffset.UtcNow)
            {
                HardwareConfiguration = hardwareConfiguration,
            };
            metadata.Validate();
            return new OpenedAntEegoStream(openedStreamId, metadata);
        }
        catch
        {
            // A failed open must never leave the single SDK stream or device open.
            try
            {
                if (openedStreamId >= 0)
                {
                    api.CloseStream(openedStreamId);
                }
            }
            catch
            {
                // Preserve the original stream-opening failure.
            }
            finally
            {
                if (amplifierOpened)
                {
                    try
                    {
                        api.CloseAmplifier(identity.NativeId);
                    }
                    catch
                    {
                        // Preserve the original stream-opening failure.
                    }
                }
            }

            throw;
        }
    }

    private void OnStreamDisposed(AntEegoStream stream)
    {
        lock (streamGate)
        {
            if (ReferenceEquals(activeStream, stream))
            {
                activeStream = null;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static string DescribeRecordedLocation(string? location) =>
        string.IsNullOrWhiteSpace(location) ? "not_recorded" : location.Trim();

    private static void AddOptionalHardwareConfiguration(
        IDictionary<string, string> configuration,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[key] = value.Trim();
        }
    }

    private sealed record OpenedAntEegoStream(int StreamId, AcquisitionStreamMetadata Metadata);
}
