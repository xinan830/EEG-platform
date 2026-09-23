
namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

public sealed record AntEegoAdapterOptions(
    string SdkLibraryPath,
    double? ReferenceRangeVolts,
    double? BipolarRangeVolts,
    TimeSpan? EmptyReadDelay = null,
    IReadOnlyDictionary<int, string>? ChannelLabelsByNativeIndex = null,
    string? HardwareReferenceElectrodeLocation = null,
    string? HardwareGroundElectrodeLocation = null,
    string? ChannelConfigurationId = null,
    string? ChannelConfigurationName = null,
    string? ChannelConfigurationSnapshotJson = null,
    string? MontageConfigurationId = null,
    string? MontageConfigurationName = null,
    string? MontageConfigurationSnapshotJson = null)
{
    public TimeSpan EffectiveEmptyReadDelay => EmptyReadDelay ?? TimeSpan.FromMilliseconds(10);

    public void ValidateSdkPath()
    {
        if (string.IsNullOrWhiteSpace(SdkLibraryPath))
        {
            throw new ArgumentException("An explicit eego-SDK.dll path is required.", nameof(SdkLibraryPath));
        }

    }

    public void ValidateForStream()
    {
        ValidateSdkPath();

        if (ReferenceRangeVolts is not > 0)
        {
            throw new ArgumentException("An explicit positive reference range in V is required.", nameof(ReferenceRangeVolts));
        }

        if (BipolarRangeVolts is not > 0)
        {
            throw new ArgumentException("An explicit positive bipolar range in V is required.", nameof(BipolarRangeVolts));
        }

        AntEegoRangePair.Validate(ReferenceRangeVolts.Value, BipolarRangeVolts.Value);

        if (EffectiveEmptyReadDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(EmptyReadDelay), "Empty read delay must be positive.");
        }
    }
}

public sealed class AntEegoSdkException(string code, string message) : AcquisitionUnavailableException(message)
{
    public string Code { get; } = code;
}

internal readonly record struct AntEegoDeviceIdentity(int NativeId)
{
    private const string Prefix = "ant-eego:";

    public string ToDeviceId() => $"{Prefix}{NativeId}";

    public static AntEegoDeviceIdentity Parse(string deviceId)
    {
        if (!deviceId.StartsWith(Prefix, StringComparison.Ordinal) ||
            !int.TryParse(deviceId[Prefix.Length..], out var nativeId) ||
            nativeId < 0)
        {
            throw new ArgumentException("The device ID is not a valid ANT/eego native identifier.", nameof(deviceId));
        }

        return new AntEegoDeviceIdentity(nativeId);
    }
}

/// <summary>
/// Converts SDK identity tokens into common device fields while retaining the
/// original serial separately in the device descriptor.
/// </summary>
internal sealed record AntEegoDeviceMetadata(string? Model, string? DeviceInstanceId)
{
    public static AntEegoDeviceMetadata Create(string? nativeType, string? serialNumber)
    {
        var rawType = Normalize(nativeType);
        var rawSerial = Normalize(serialNumber);
        return new AntEegoDeviceMetadata(FormatModel(rawType), ExtractInstanceId(rawType, rawSerial));
    }

    private static string? FormatModel(string? nativeType)
    {
        if (nativeType is not { Length: > 2 } ||
            !nativeType.StartsWith("EE", StringComparison.OrdinalIgnoreCase) ||
            !nativeType[2..].All(char.IsAsciiDigit))
        {
            return nativeType;
        }

        return $"EE-{nativeType[2..]}";
    }

    private static string? ExtractInstanceId(string? nativeType, string? serialNumber)
    {
        if (serialNumber is null || nativeType is null)
        {
            return serialNumber;
        }

        var prefix = $"{nativeType}-";
        return serialNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? serialNumber[prefix.Length..]
            : serialNumber;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal enum AntEegoNativeChannelType
{
    Reference = 0,
    Bipolar = 1,
    Accelerometer = 2,
    Gyroscope = 3,
    Magnetometer = 4,
    Trigger = 5,
    SampleCounter = 6,
    ImpedanceReference = 7,
    ImpedanceGround = 8,
}

internal static class AntEegoChannelMapper
{
    public static AcquisitionChannel Map(
        int streamIndex,
        int nativeChannelIndex,
        AntEegoNativeChannelType nativeType,
        IReadOnlyDictionary<int, string>? channelLabelsByNativeIndex = null) =>
        new(
            streamIndex,
            nativeChannelIndex,
            Label: channelLabelsByNativeIndex?.GetValueOrDefault(nativeChannelIndex),
            nativeType switch
            {
                AntEegoNativeChannelType.Reference => AcquisitionChannelKind.Reference,
                AntEegoNativeChannelType.Bipolar => AcquisitionChannelKind.Bipolar,
                AntEegoNativeChannelType.Trigger => AcquisitionChannelKind.Trigger,
                AntEegoNativeChannelType.SampleCounter => AcquisitionChannelKind.SampleCounter,
                AntEegoNativeChannelType.ImpedanceReference => AcquisitionChannelKind.ImpedanceReference,
                AntEegoNativeChannelType.ImpedanceGround => AcquisitionChannelKind.ImpedanceGround,
                AntEegoNativeChannelType.Accelerometer => AcquisitionChannelKind.Accelerometer,
                AntEegoNativeChannelType.Gyroscope => AcquisitionChannelKind.Gyroscope,
                AntEegoNativeChannelType.Magnetometer => AcquisitionChannelKind.Magnetometer,
                _ => AcquisitionChannelKind.Unknown,
            },
            nativeType switch
            {
                AntEegoNativeChannelType.Reference or AntEegoNativeChannelType.Bipolar => "V",
                AntEegoNativeChannelType.Trigger => "code",
                AntEegoNativeChannelType.SampleCounter => "count",
                AntEegoNativeChannelType.ImpedanceReference or AntEegoNativeChannelType.ImpedanceGround => "ohm",
                _ => "vendor_native",
            });
}

internal static class AntEegoSampleCounterReader
{
    public static long ReadAndValidateFirstCounter(
        double[] sampleMajorValues,
        int sampleCount,
        int channelCount,
        int sampleCounterStreamIndex)
    {
        if (sampleCounterStreamIndex < 0 || sampleCounterStreamIndex >= channelCount)
        {
            throw new AcquisitionContinuityException("Sample counter stream position is invalid.");
        }

        long? firstCounter = null;
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var value = sampleMajorValues[checked(sampleIndex * channelCount + sampleCounterStreamIndex)];
            if (!double.IsFinite(value) || value < 0 || value != Math.Truncate(value) || value > long.MaxValue)
            {
                throw new AcquisitionContinuityException("Device sample counter contains a non-integral or invalid value.");
            }

            var counter = checked((long)value);
            if (firstCounter is { } first && counter != checked(first + sampleIndex))
            {
                throw new AcquisitionContinuityException("Device sample counter is not consecutive within a received batch.");
            }

            firstCounter ??= counter;
        }

        return firstCounter ?? throw new AcquisitionContinuityException("Received an empty counter batch.");
    }
}
