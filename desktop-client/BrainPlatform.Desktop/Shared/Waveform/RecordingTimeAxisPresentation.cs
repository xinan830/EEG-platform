using System.ComponentModel;
using System.Windows;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;

namespace BrainPlatform.Desktop.Shared.Waveform;

/// <summary>Opt-in formatting for time axes; never changes series or visible ranges.</summary>
public static class RecordingTimeAxisPresentation
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.RegisterAttached(
        "Context", typeof(RecordingTimeDisplayContext), typeof(RecordingTimeAxisPresentation),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnContextChanged));
    private static readonly DependencyProperty BindingProperty = DependencyProperty.RegisterAttached(
        "Binding", typeof(AxisBinding), typeof(RecordingTimeAxisPresentation));

    public static RecordingTimeDisplayContext? GetContext(DependencyObject host) =>
        (RecordingTimeDisplayContext?)host.GetValue(ContextProperty);
    public static void SetContext(DependencyObject host, RecordingTimeDisplayContext? value) =>
        host.SetValue(ContextProperty, value);

    public static void SetAxis(FrameworkElement host, NumericAxis? axis)
    {
        if (host.GetValue(BindingProperty) is AxisBinding previous)
        {
            if (ReferenceEquals(previous.Axis, axis)) { previous.Refresh(); return; }
            previous.Detach();
        }
        host.SetValue(BindingProperty, axis is null ? null : new AxisBinding(host, axis));
    }

    private static void OnContextChanged(DependencyObject host, DependencyPropertyChangedEventArgs args) =>
        (host.GetValue(BindingProperty) as AxisBinding)?.Refresh();

    private sealed class AxisBinding
    {
        private readonly FrameworkElement host;
        private readonly ILabelProvider originalLabels;
        private readonly string originalTitle;
        private RecordingTimeDisplayContext? context;

        public AxisBinding(FrameworkElement host, NumericAxis axis)
        {
            this.host = host;
            Axis = axis;
            originalLabels = axis.LabelProvider;
            originalTitle = axis.AxisTitle;
            Refresh();
        }

        public NumericAxis Axis { get; }

        public void Refresh()
        {
            var current = GetContext(host);
            if (!ReferenceEquals(context, current))
            {
                if (context is not null) PropertyChangedEventManager.RemoveHandler(context, OnDisplayChanged, string.Empty);
                context = current;
                if (context is not null) PropertyChangedEventManager.AddHandler(context, OnDisplayChanged, string.Empty);
            }
            Apply();
        }

        public void Detach()
        {
            if (context is not null) PropertyChangedEventManager.RemoveHandler(context, OnDisplayChanged, string.Empty);
            Axis.LabelProvider = originalLabels;
            Axis.AxisTitle = originalTitle;
            Axis.InvalidateElement();
        }

        private void OnDisplayChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(RecordingTimeDisplayContext.EffectiveMode)) Apply();
        }

        private void Apply()
        {
            Axis.LabelProvider = context is null ? originalLabels : new TimeLabels(context);
            Axis.AxisTitle = context?.IsClockTime == true
                ? originalTitle is "记录时间（s）" or "时间（s）" ? "采集时间"
                    : originalTitle.Replace("（s）", "（采集时间）", StringComparison.Ordinal)
                : originalTitle;
            Axis.InvalidateElement();
        }
    }

    private sealed class TimeLabels(RecordingTimeDisplayContext context) : NumericLabelProvider
    {
        public override string FormatLabel(IComparable dataValue) => context.FormatAxis(Convert.ToDouble(dataValue));
        public override string FormatCursorLabel(IComparable dataValue) => context.FormatCursor(Convert.ToDouble(dataValue));
    }
}
