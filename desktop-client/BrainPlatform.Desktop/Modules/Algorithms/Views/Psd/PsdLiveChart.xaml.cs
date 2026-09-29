using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SciChart.Charting;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Psd;

public partial class PsdLiveChart : UserControl
{
    private INotifyCollectionChanged? observedCollection;
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new()
    {
        AutoRange = SciChart.Charting.Visuals.Axes.AutoRange.Never,
        AxisTitle = "频率（Hz）",
        FontSize = 14,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        DrawMajorBands = false,
        DrawMajorGridLines = false,
        DrawMinorGridLines = false,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left,
        AutoRange = SciChart.Charting.Visuals.Axes.AutoRange.Always,
        GrowBy = new DoubleRange(0.0, 0.08),
        AxisTitle = "功率谱密度（μV²/Hz）",
        FontSize = 14,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        DrawMajorBands = false,
        DrawMajorGridLines = false,
        DrawMinorGridLines = false,
    };
    private readonly XyDataSeries<double, double> dataSeries = new() { SeriesName = "功率谱密度" };
    private readonly FastLineRenderableSeries lineSeries;
    private readonly BoxAnnotation[] frequencyBands;
    private readonly TextAnnotation[] frequencyLabels;
    private readonly RolloverModifier rollover = new()
    {
        IsEnabled = true,
        ReceiveHandledEvents = true,
        DrawVerticalLine = true,
        ShowTooltipOn = ShowTooltipOptions.MouseOver,
    };

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(PsdLiveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(PsdLiveChart), new PropertyMetadata("μV²/Hz", OnUnitChanged));

    public PsdLiveChart()
    {
        InitializeComponent();
        // The page owns the axis-unit labels. Keep SciChart's internal titles empty
        // so the chart does not render a second set of units.
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.GridLinesPanelStyle = new Style(typeof(Control));
        lineSeries = new FastLineRenderableSeries { DataSeries = dataSeries, Stroke = Colors.RoyalBlue, StrokeThickness = 2 };
        frequencyBands =
        [
            CreateBand(0, 4, "#EFF6FF"),
            CreateBand(4, 8, "#ECFDF5"),
            CreateBand(8, 13, "#FFF1F2"),
            CreateBand(13, 30, "#FFFBEB"),
            CreateBand(30, 50, "#F5F3FF"),
        ];
        frequencyLabels =
        [
            CreateLabel(2, "δ Delta\n0.5 – 4 Hz", "#1D4ED8"),
            CreateLabel(6, "θ Theta\n4 – 8 Hz", "#059669"),
            CreateLabel(10.5, "α Alpha\n8 – 13 Hz", "#DC2626"),
            CreateLabel(21.5, "β Beta\n13 – 30 Hz", "#D97706"),
            CreateLabel(40, "γ Gamma\n30 – 50 Hz", "#7C3AED"),
        ];
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        foreach (var band in frequencyBands)
            surface.Annotations.Add(band);
        foreach (var label in frequencyLabels)
            surface.Annotations.Add(label);
        surface.RenderableSeries.Add(lineSeries);
        surface.ChartModifier = rollover;
        surface.LayoutUpdated += (_, _) => UpdateBandRange();
        Content = surface;
    }

    private static BoxAnnotation CreateBand(double x1, double x2, string color) => new()
    {
        X1 = x1,
        X2 = x2,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = 0.55 },
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        AnnotationCanvas = AnnotationCanvas.BelowChart,
        IsEditable = false,
    };

    private static TextAnnotation CreateLabel(double x, string text, string color) => new()
    {
        X1 = x,
        Y1 = 0,
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
        Background = Brushes.Transparent,
        BorderBrush = Brushes.Transparent,
        IsEditable = false,
        AnnotationCanvas = AnnotationCanvas.AboveChart,
        HorizontalAnchorPoint = HorizontalAnchorPoint.Center,
        VerticalAnchorPoint = VerticalAnchorPoint.Top,
        TextAlignment = TextAlignment.Center,
    };

    private void UpdateBandRange()
    {
        if (yAxis.VisibleRange is not DoubleRange range || !double.IsFinite(range.Min) || !double.IsFinite(range.Max))
            return;
        foreach (var band in frequencyBands)
        {
            band.Y1 = range.Min;
            band.Y2 = range.Max;
        }
        foreach (var label in frequencyLabels)
            label.Y1 = range.Max;
    }



    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (PsdLiveChart)d;
        chart.DetachCollection();
        chart.AttachCollection(e.NewValue as IEnumerable);
        chart.RebuildSeries();
    }

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }

    private void AttachCollection(IEnumerable? value)
    {
        if (value is INotifyCollectionChanged collection)
        {
            observedCollection = collection;
            observedCollection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachCollection()
    {
        if (observedCollection is not null) observedCollection.CollectionChanged -= OnCollectionChanged;
        observedCollection = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSeries();

    private void RebuildSeries()
    {
        var values = Points?.Cast<object>().Select(item =>
        {
            var type = item.GetType();
            var frequency = type.GetProperty("Frequency")?.GetValue(item) as double?;
            var value = type.GetProperty("Value")?.GetValue(item) as double?;
            return frequency is { } f && value is { } v && double.IsFinite(f) && double.IsFinite(v) && f is >= 0 and <= 50
                ? (Frequency: f, Value: v) : ((double Frequency, double Value)?)null;
        }).Where(point => point is not null).Select(point => point!.Value).OrderBy(point => point.Frequency).ToArray() ?? [];

        dataSeries.Clear();
        dataSeries.Append(values.Select(point => point.Frequency), values.Select(point => point.Value));
        xAxis.VisibleRange = new DoubleRange(0, 50);
            UpdateBandRange();
    }
}
