using System.Globalization;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Rbp;

public sealed class RbpChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly List<XyDataSeries<double, double>> stackedSeries = [];
    private INotifyCollectionChanged? stackedWindowsSource;
    private readonly NumericAxis xAxis = new()
    {
        FontSize = 13, DrawMajorBands = false, DrawMajorGridLines = false,
        DrawMinorGridLines = false, AutoRange = AutoRange.Never,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left, AxisTitle = "相对功率（%）",
        TextFormatting = "0%", FontSize = 13, DrawMajorBands = false,
        DrawMajorGridLines = true, DrawMinorGridLines = false,
        AutoRange = AutoRange.Never, VisibleRange = new DoubleRange(0, 1.05),
    };

    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(IReadOnlyList<RbpBandPoint>), typeof(RbpChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty WindowsProperty = DependencyProperty.Register(
        nameof(Windows), typeof(IReadOnlyList<RbpWindowPoint>), typeof(RbpChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty IsTrendProperty = DependencyProperty.Register(
        nameof(IsTrend), typeof(bool), typeof(RbpChart), new PropertyMetadata(false, OnDataChanged));
    public static readonly DependencyProperty IsStackedHistoryProperty = DependencyProperty.Register(
        nameof(IsStackedHistory), typeof(bool), typeof(RbpChart), new PropertyMetadata(false, OnDataChanged));

    public IReadOnlyList<RbpBandPoint>? Bands { get => (IReadOnlyList<RbpBandPoint>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public IReadOnlyList<RbpWindowPoint>? Windows { get => (IReadOnlyList<RbpWindowPoint>?)GetValue(WindowsProperty); set => SetValue(WindowsProperty, value); }
    public bool IsTrend { get => (bool)GetValue(IsTrendProperty); set => SetValue(IsTrendProperty, value); }
    public bool IsStackedHistory { get => (bool)GetValue(IsStackedHistoryProperty); set => SetValue(IsStackedHistoryProperty, value); }

    public RbpChart()
    {
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true, DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
        Content = surface;
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RbpChart)sender).Rebuild();

    private void Rebuild()
    {
        if (stackedWindowsSource is not null)
            stackedWindowsSource.CollectionChanged -= OnStackedWindowsChanged;
        stackedWindowsSource = null;
        stackedSeries.Clear();
        RecordingTimeAxisPresentation.SetAxis(this, null);
        surface.RenderableSeries.Clear();
        if (IsStackedHistory)
        {
            BuildStackedHistory();
            return;
        }
        if (IsTrend)
        {
            BuildTrend();
            return;
        }
        BuildBars();
    }

    private void BuildStackedHistory()
    {
        var windows = Windows ?? [];
        var bands = Bands ?? [];
        xAxis.AxisTitle = "窗口结束时间（s）";
        xAxis.LabelProvider = new NumericLabelProvider();
        xAxis.AutoTicks = true;
        xAxis.VisibleRange = windows.Count > 0
            ? new DoubleRange(Math.Max(0, windows[0].StartSeconds), Math.Max(windows[0].StartSeconds + 1, windows[^1].EndSeconds + 0.5))
            : new DoubleRange(0, 10);
        RecordingTimeAxisPresentation.SetAxis(this, xAxis);
        foreach (var (band, index) in bands.Select((value, index) => (value, index)))
        {
            var values = new XyDataSeries<double, double> { SeriesName = band.Label };
            foreach (var window in windows)
                values.Append(window.EndSeconds, window.Shares.ElementAtOrDefault(index) ?? double.NaN);
            stackedSeries.Add(values);
            surface.RenderableSeries.Add(new StackedMountainRenderableSeries
            {
                DataSeries = values,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(band.Color)),
                Stroke = (Color)ColorConverter.ConvertFromString(band.Color),
                StrokeThickness = 0,
                StackedGroupId = "rbp-bands",
                // RBP values are backend-computed shares that already sum to 1.
                // Re-normalizing each timestamp can turn malformed/missing bands
                // into a visually plausible but scientifically false 100% stack.
                IsOneHundredPercent = false,
                DrawNaNAs = SciChart.Charting.Visuals.RenderableSeries.LineDrawMode.Gaps,
            });
        }
        stackedWindowsSource = Windows as INotifyCollectionChanged;
        if (stackedWindowsSource is not null)
            stackedWindowsSource.CollectionChanged += OnStackedWindowsChanged;
    }

    private void OnStackedWindowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (stackedSeries.Count == 0 || Bands is not { Count: > 0 } bands || Windows is not { Count: > 0 } windows)
        {
            Rebuild();
            return;
        }
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex == windows.Count - 1 &&
            args.NewItems is { Count: 1 } added)
        {
            AppendStackedWindow((RbpWindowPoint)added[0]!);
            return;
        }
        if (args.Action == NotifyCollectionChangedAction.Remove && args.OldStartingIndex == 0 &&
            args.OldItems is { Count: 1 } && stackedSeries.All(series => series.Count == windows.Count + 1))
        {
            foreach (var series in stackedSeries) series.RemoveAt(0);
            UpdateStackedRange(windows);
            return;
        }
        if (args.Action == NotifyCollectionChangedAction.Replace && args.NewStartingIndex >= 0 &&
            args.NewItems is { Count: 1 } replacement)
        {
            var window = (RbpWindowPoint)replacement[0]!;
            for (var band = 0; band < stackedSeries.Count; band++)
                stackedSeries[band].Update(args.NewStartingIndex, window.Shares.ElementAtOrDefault(band) ?? double.NaN);
            return;
        }
        Rebuild();
    }

    private void AppendStackedWindow(RbpWindowPoint window)
    {
        if (stackedSeries.Count != Bands!.Count)
        {
            Rebuild();
            return;
        }
        using (surface.SuspendUpdates())
        {
            for (var band = 0; band < stackedSeries.Count; band++)
                stackedSeries[band].Append(window.EndSeconds, window.Shares.ElementAtOrDefault(band) ?? double.NaN);
            UpdateStackedRange(Windows!);
        }
    }

    private void UpdateStackedRange(IReadOnlyList<RbpWindowPoint> windows)
    {
        xAxis.VisibleRange = windows.Count > 0
            ? new DoubleRange(Math.Max(0, windows[0].StartSeconds),
                Math.Max(windows[0].StartSeconds + 1, windows[^1].EndSeconds + 0.5))
            : new DoubleRange(0, 10);
    }

    private void BuildBars()
    {
        var bands = Bands ?? [];
        xAxis.AxisTitle = "频段";
        xAxis.LabelProvider = new BandLabelProvider(bands.Select(band => band.Label).ToArray());
        xAxis.AutoTicks = false;
        xAxis.MajorDelta = 1;
        xAxis.MinorDelta = 1;
        xAxis.VisibleRange = new DoubleRange(-0.6, Math.Max(0.6, bands.Count - 0.4));
        foreach (var (band, index) in bands.Select((value, index) => (value, index)))
        {
            if (band.Share is not double share) continue;
            var points = new XyDataSeries<double, double> { SeriesName = $"{band.Label} · {band.ShareText}" };
            points.Append(index, share);
            surface.RenderableSeries.Add(new FastColumnRenderableSeries
            {
                DataSeries = points,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(band.Color)),
                StrokeThickness = 0,
                DataPointWidth = 0.6,
            });
        }
    }

    private void BuildTrend()
    {
        var windows = Windows ?? [];
        var bands = Bands ?? [];
        xAxis.AxisTitle = "窗口结束时间（s）";
        xAxis.LabelProvider = new NumericLabelProvider();
        xAxis.AutoTicks = true;
        xAxis.VisibleRange = windows.Count > 0
            ? new DoubleRange(Math.Max(0, windows[0].StartSeconds), Math.Max(windows[0].StartSeconds + 1, windows[^1].EndSeconds + 0.5))
            : new DoubleRange(0, 10);
        RecordingTimeAxisPresentation.SetAxis(this, xAxis);
        for (var index = 0; index < bands.Count; index++)
        {
            var points = new XyDataSeries<double, double> { SeriesName = bands[index].Label };
            foreach (var window in windows)
                points.Append(window.EndSeconds, window.Shares[index] ?? double.NaN);
            surface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                DataSeries = points,
                Stroke = (Color)ColorConverter.ConvertFromString(bands[index].Color),
                StrokeThickness = 2,
                DrawNaNAs = LineDrawMode.Gaps,
            });
        }
    }

    private sealed class BandLabelProvider(string[] labels) : NumericLabelProvider
    {
        public override string FormatLabel(IComparable dataValue)
        {
            var value = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
            var index = (int)Math.Round(value);
            return Math.Abs(value - index) < 1e-6 && index >= 0 && index < labels.Length ? labels[index] : "";
        }
    }
}
