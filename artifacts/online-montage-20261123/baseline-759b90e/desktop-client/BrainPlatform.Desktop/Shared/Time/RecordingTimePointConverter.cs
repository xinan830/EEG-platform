using System.Globalization;
using System.Windows.Data;

namespace BrainPlatform.Desktop.Shared.Time;

public sealed class RecordingTimePointConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is double seconds && values[1] is RecordingTimeDisplayContext context
            ? context.FormatPoint(seconds) : "--";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
