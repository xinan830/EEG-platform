using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Shared.Waveform;

/// <summary>Shared review-style display. Owns no HTTP, playback, filters or algorithm computations.</summary>
public sealed class SignalWaveformView : UserControl
{
    private readonly SciChartSurface surface = new() { Background = Brushes.White, ShowLicensingWarnings = false };
    private readonly NumericAxis x = new() { AxisTitle = "记录时间 (s)", AutoRange = AutoRange.Never, DrawMajorBands = false,
        DrawMajorGridLines = false, DrawMinorGridLines = false, DrawMinorTicks = false, TextFormatting = "0.0" };
    private readonly Grid labels = new();
    private readonly TextBlock status = new() { Text = "等待记录波形", HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray };
    private readonly VerticalLineAnnotation cursor = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 1, IsEditable = false };
    private Thickness? labelMargin;
    public event EventHandler<double>? SeekRequested;
    public static readonly DependencyProperty SignalProperty = DependencyProperty.Register(nameof(Signal), typeof(SignalPreviewResponse),
        typeof(SignalWaveformView), new PropertyMetadata(null, (owner, _) => ((SignalWaveformView)owner).Refresh()));
    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(nameof(PositionSeconds), typeof(double),
        typeof(SignalWaveformView), new PropertyMetadata(0d, (owner, _) => ((SignalWaveformView)owner).UpdateCursor()));
    public static readonly DependencyProperty LaneMicrovoltsProperty = DependencyProperty.Register(nameof(LaneMicrovolts), typeof(double),
        typeof(SignalWaveformView), new PropertyMetadata(200d, (owner, _) => ((SignalWaveformView)owner).Refresh()),
        value => value is double scale && double.IsFinite(scale) && scale > 0);
    public SignalPreviewResponse? Signal { get => (SignalPreviewResponse?)GetValue(SignalProperty); set => SetValue(SignalProperty, value); }
    public double PositionSeconds { get => (double)GetValue(PositionSecondsProperty); set => SetValue(PositionSecondsProperty, value); }
    public double LaneMicrovolts { get => (double)GetValue(LaneMicrovoltsProperty); set => SetValue(LaneMicrovoltsProperty, value); }

    public SignalWaveformView()
    {
        SciChart.Charting.ThemeManager.SetTheme(surface, "BrightSpark");
        surface.XAxes.Add(x);
        RecordingTimeAxisPresentation.SetAxis(this, x);
        surface.ChartModifier = new ModifierGroup(new MouseWheelZoomModifier(), new ZoomPanModifier(),
            new ZoomExtentsModifier(), new RolloverModifier { ShowTooltipOn = ShowTooltipOptions.MouseOver, DrawVerticalLine = true });
        var root = WaveformPlotLayout.CreateRootGrid();
        root.Children.Add(labels);
        var host = new Grid(); Grid.SetColumn(host, 2); host.Children.Add(surface); host.Children.Add(status); root.Children.Add(host);
        Content = root;
        surface.LayoutUpdated += (_, _) => WaveformPlotLayout.SyncLabelPlotArea(surface, labels, ref labelMargin);
        surface.MouseLeftButtonUp += (_, args) =>
        {
            if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) || Signal is null) return;
            var seconds = x.GetCurrentCoordinateCalculator().GetDataValue(args.GetPosition((IInputElement)surface.GridLinesPanel).X);
            if (double.IsFinite(seconds)) SeekRequested?.Invoke(this, seconds);
        };
    }

    private void UpdateCursor()
    {
        cursor.X1 = PositionSeconds;
        if (Signal is { TimeSeconds.Count: > 1 } buffer)
        {
            var start = Math.Clamp(PositionSeconds - 5, buffer.TimeSeconds[0], Math.Max(buffer.TimeSeconds[0], buffer.TimeSeconds[^1] - 10));
            x.VisibleRange = new DoubleRange(start, Math.Min(start + 10, buffer.TimeSeconds[^1]));
        }
        cursor.Visibility = Signal is { TimeSeconds.Count: > 0 } signal && PositionSeconds >= signal.TimeSeconds[0] &&
            PositionSeconds <= signal.TimeSeconds[^1] ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        using (surface.SuspendUpdates())
        {
            surface.RenderableSeries.Clear(); surface.YAxes.Clear(); surface.Annotations.Clear();
            labels.Children.Clear(); labels.RowDefinitions.Clear(); status.Visibility = Visibility.Visible;
            if (Signal is not { TimeSeconds.Count: > 1 } signal) { status.Text = "等待加载波形"; return; }
            if (signal.Unit != "uV" || signal.Channels.Count == 0 || signal.TimeSeconds.Any(time => !double.IsFinite(time)) ||
                signal.Channels.Values.Any(samples => samples.Length != signal.TimeSeconds.Count || samples.Any(value => !double.IsFinite(value))) ||
                !HasValidExtrema(signal))
            { status.Text = "波形单位、维度或数值无效"; return; }
            Color[] colors = [Colors.IndianRed, Colors.SteelBlue, Colors.ForestGreen, Colors.MediumOrchid, Colors.DarkOrange];
            var index = 0;
            foreach (var channel in signal.Channels)
            {
                var axisId = $"signal-{index}";
                // Fixed common amplitude scale. Never normalize each channel to its own amplitude.
                var offset = (signal.Channels.Count - index - 1) * LaneMicrovolts;
                surface.YAxes.Add(new NumericAxis { Id = axisId, AutoRange = AutoRange.Never,
                    VisibleRange = new DoubleRange(-offset - LaneMicrovolts / 2, -offset - LaneMicrovolts / 2 + signal.Channels.Count * LaneMicrovolts),
                    Visibility = Visibility.Collapsed, DrawMajorBands = false, DrawMajorGridLines = false, DrawMinorGridLines = false });
                var series = new XyDataSeries<double, double> { SeriesName = $"{channel.Key} · µV" };
                series.Append(signal.TimeSeconds, channel.Value);
                AddExtremaEnvelope(signal, channel.Key, axisId, colors[index % colors.Length]);
                surface.RenderableSeries.Add(new FastLineRenderableSeries { YAxisId = axisId, DataSeries = series,
                    Stroke = colors[index % colors.Length], StrokeThickness = 1, DrawNaNAs = LineDrawMode.Gaps });
                labels.RowDefinitions.Add(new RowDefinition());
                var label = new TextBlock { Text = channel.Key, Foreground = new SolidColorBrush(colors[index % colors.Length]),
                    VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new(2, 0, 3, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"{channel.Key} · 轨道间距 {LaneMicrovolts:G} µV" };
                Grid.SetRow(label, index++); labels.Children.Add(label);
            }
            surface.Annotations.Add(new LineAnnotation
            {
                YAxisId = "signal-0", CoordinateMode = AnnotationCoordinateMode.RelativeX, X1 = .015, X2 = .015,
                Y1 = -(signal.Channels.Count - .5) * LaneMicrovolts + 20,
                Y2 = -(signal.Channels.Count - .5) * LaneMicrovolts + 120,
                Stroke = Brushes.DimGray, StrokeThickness = 2
            });
            surface.Annotations.Add(new TextAnnotation
            {
                YAxisId = "signal-0", CoordinateMode = AnnotationCoordinateMode.RelativeX, X1 = .025,
                Y1 = -(signal.Channels.Count - .5) * LaneMicrovolts + 120,
                Text = "100 µV", Foreground = Brushes.DimGray, FontSize = 11
            });
            surface.Annotations.Add(cursor); UpdateCursor(); status.Visibility = Visibility.Collapsed;
        }
    }

    private void AddExtremaEnvelope(SignalPreviewResponse signal, string channelName, string axisId, Color color)
    {
        var extremaTimes = signal.ExtremaTimeSeconds;
        var channelExtrema = signal.ChannelExtrema;
        if (extremaTimes is not { Count: > 0 } times || channelExtrema is null ||
            !channelExtrema.TryGetValue(channelName, out var extrema)) return;

        var points = SignalPreviewExtremaProjection.ToVerticalSegments(times, extrema);
        var data = new XyDataSeries<double, double> { SeriesName = $"{channelName} · 每显示格范围" };
        data.Append(points.TimeSeconds, points.Microvolts);
        var envelopeColor = Color.FromArgb(105, color.R, color.G, color.B);
        surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            YAxisId = axisId,
            DataSeries = data,
            Stroke = envelopeColor,
            StrokeThickness = 2,
            DrawNaNAs = LineDrawMode.Gaps,
        });
    }

    private static bool HasValidExtrema(SignalPreviewResponse signal)
    {
        if (signal.ExtremaTimeSeconds is null && signal.ChannelExtrema is null) return true;
        if (signal.ExtremaTimeSeconds is not { Count: > 0 } times ||
            signal.ChannelExtrema is not { Count: > 0 } extremaByChannel ||
            times.Any(time => !double.IsFinite(time)) ||
            extremaByChannel.Count != signal.Channels.Count ||
            extremaByChannel.Keys.Any(channel => !signal.Channels.ContainsKey(channel))) return false;

        return extremaByChannel.Values.All(extrema =>
            extrema.MinimumMicrovolts.Count == times.Count &&
            extrema.MaximumMicrovolts.Count == times.Count &&
            extrema.MinimumMicrovolts.Zip(extrema.MaximumMicrovolts).All(pair =>
                double.IsFinite(pair.First) && double.IsFinite(pair.Second) && pair.First <= pair.Second));
    }
}
