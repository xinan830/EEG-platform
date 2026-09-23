using System.Globalization;
using System.Windows.Data;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

/// <summary>Formats the device-reported V range for a human-facing hardware setting.</summary>
public sealed class InputRangeVoltageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double volts || volts <= 0) return "未读取";
        var microvolts = volts * 1_000_000d;
        return microvolts < 1_000_000d ? $"±{microvolts:g} µV" : $"±{volts:g} V";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
