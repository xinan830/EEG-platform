using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

/// <summary>Converts the generic connection envelope into ANT/eego SDK options.</summary>
public sealed class AntEegoAcquisitionDriver : IAcquisitionDeviceDriver
{
    public const string DriverId = "ant-eego";
    public const string SdkLibraryPathSetting = "sdk_library_path";
    public const string ReferenceRangeVoltsSetting = "reference_range_volts";
    public const string BipolarRangeVoltsSetting = "bipolar_range_volts";
    public const string HardwareReferenceElectrodeLocationSetting = "hardware_reference_electrode_location";
    public const string HardwareGroundElectrodeLocationSetting = "hardware_ground_electrode_location";
    public const string ChannelConfigurationIdSetting = "channel_configuration_id";
    public const string ChannelConfigurationNameSetting = "channel_configuration_name";
    public const string ChannelConfigurationSnapshotSetting = "channel_configuration_snapshot_json";
    public const string MontageConfigurationIdSetting = "montage_configuration_id";
    public const string MontageConfigurationNameSetting = "montage_configuration_name";
    public const string MontageConfigurationSnapshotSetting = "montage_configuration_snapshot_json";

    public AcquisitionDriverDescriptor Descriptor { get; } = new(
        DriverId,
        "ANT/eego",
        "选择本机 eego-SDK.dll 后测试已连接的 ANT 放大器。");

    public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.Equals(configuration.DriverId, DriverId, StringComparison.Ordinal))
        {
            throw new AcquisitionUnavailableException($"ANT/eego 驱动不能处理“{configuration.DriverId}”配置。");
        }

        return new AntEegoAcquisitionAdapter(new AntEegoAdapterOptions(
            configuration.RequireSetting(SdkLibraryPathSetting),
            ParseOptionalVoltage(configuration.GetOptionalSetting(ReferenceRangeVoltsSetting), ReferenceRangeVoltsSetting),
            ParseOptionalVoltage(configuration.GetOptionalSetting(BipolarRangeVoltsSetting), BipolarRangeVoltsSetting),
            ChannelLabelsByNativeIndex: configuration.ChannelLabelsByNativeIndex,
            HardwareReferenceElectrodeLocation: configuration.GetOptionalSetting(HardwareReferenceElectrodeLocationSetting),
            HardwareGroundElectrodeLocation: configuration.GetOptionalSetting(HardwareGroundElectrodeLocationSetting),
            ChannelConfigurationId: configuration.GetOptionalSetting(ChannelConfigurationIdSetting),
            ChannelConfigurationName: configuration.GetOptionalSetting(ChannelConfigurationNameSetting),
            ChannelConfigurationSnapshotJson: configuration.GetOptionalSetting(ChannelConfigurationSnapshotSetting),
            MontageConfigurationId: configuration.GetOptionalSetting(MontageConfigurationIdSetting),
            MontageConfigurationName: configuration.GetOptionalSetting(MontageConfigurationNameSetting),
            MontageConfigurationSnapshotJson: configuration.GetOptionalSetting(MontageConfigurationSnapshotSetting)));
    }

    public static AcquisitionDriverConfiguration CreateConfiguration(AntEegoAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SdkLibraryPathSetting] = options.SdkLibraryPath,
        };
        if (options.ReferenceRangeVolts is { } referenceRange)
        {
            settings[ReferenceRangeVoltsSetting] = referenceRange.ToString("R", CultureInfo.InvariantCulture);
        }

        if (options.BipolarRangeVolts is { } bipolarRange)
        {
            settings[BipolarRangeVoltsSetting] = bipolarRange.ToString("R", CultureInfo.InvariantCulture);
        }

        AddOptionalTextSetting(
            settings,
            HardwareReferenceElectrodeLocationSetting,
            options.HardwareReferenceElectrodeLocation);
        AddOptionalTextSetting(
            settings,
            HardwareGroundElectrodeLocationSetting,
            options.HardwareGroundElectrodeLocation);
        AddOptionalTextSetting(settings, ChannelConfigurationIdSetting, options.ChannelConfigurationId);
        AddOptionalTextSetting(settings, ChannelConfigurationNameSetting, options.ChannelConfigurationName);
        AddOptionalTextSetting(settings, ChannelConfigurationSnapshotSetting, options.ChannelConfigurationSnapshotJson);
        AddOptionalTextSetting(settings, MontageConfigurationIdSetting, options.MontageConfigurationId);
        AddOptionalTextSetting(settings, MontageConfigurationNameSetting, options.MontageConfigurationName);
        AddOptionalTextSetting(settings, MontageConfigurationSnapshotSetting, options.MontageConfigurationSnapshotJson);

        return new AcquisitionDriverConfiguration(DriverId, settings, options.ChannelLabelsByNativeIndex);
    }

    private static double? ParseOptionalVoltage(string? rawValue, string settingName)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            throw new AcquisitionUnavailableException($"ANT/eego 连接参数“{settingName}”必须是有限电压数值。");
        }

        return value;
    }

    private static void AddOptionalTextSetting(
        IDictionary<string, string> settings,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            settings[key] = value.Trim();
        }
    }
}
