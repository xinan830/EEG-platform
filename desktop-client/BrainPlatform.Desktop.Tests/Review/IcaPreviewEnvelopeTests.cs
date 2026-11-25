using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class IcaPreviewEnvelopeTests
{
    [Fact]
    public void JsonEnvelope_PreservesNullGapsTimesAndDimensionlessUnits()
    {
        var item = JsonSerializer.Deserialize<IcaComponentItem>("""
            {"component_index":0,"eog_score":0.8,"ecg_score":null,"explained_variance_ratio":0.2,
             "recommended_exclude":true,"excluded":false,
             "preview_waveform":[0,12,null,-2],"preview_times_s":[0,0.17,1.2,9.998],
             "preview_duration_s":10,"preview_unit":"z-score",
             "preview_compression":{"method":"finite_run_min_max_envelope_v1","scientific_resampling":false,
               "target_points_per_channel":240,"gap_boundaries_may_exceed_target":true}}
            """)!;
        var card = new IcaComponentCardViewModel(item, false);
        Assert.Equal(new[] { 0, .17, 1.2, 9.998 }, card.PreviewTimesSeconds);
        Assert.True(double.IsNaN(card.PreviewWaveform[2]));
        Assert.Equal(12, card.PreviewWaveform[1]);
        Assert.Throws<InvalidOperationException>(() => (item with { PreviewTimesSeconds = null }).ValidatePreview());
        Assert.Throws<InvalidOperationException>(() => (item with { PreviewUnit = "uV" }).ValidatePreview());
        Assert.Throws<InvalidOperationException>(() => (item with { PreviewTimesSeconds = [0, 1, .5, 2] }).ValidatePreview());
        var dialog = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "IcaReviewDialog.xaml"));
        var sparkline = Assert.Single(dialog.Descendants(), element => element.Name.LocalName == "IcSparklineControl");
        Assert.Equal("{Binding PreviewWaveform}", (string?)sparkline.Attribute("Samples"));
        Assert.Equal("{Binding PreviewTimesSeconds}", (string?)sparkline.Attribute("TimesSeconds"));
        Assert.Equal("{Binding PreviewDurationSeconds}", (string?)sparkline.Attribute("DurationSeconds"));
    }

    [Fact]
    public void Sparkline_UsesNonuniformSecondsAndBreaksAtGaps()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new IcSparklineControl
                { Samples = [0, 10, double.NaN, -5, 0, double.NaN], TimesSeconds = [0, .2, 1.2, 1.4, 4, 5], DurationSeconds = 10 };
                control.Measure(new Size(206, 40));
                control.Arrange(new Rect(0, 0, 206, 40));
                var drawing = VisualTreeHelper.GetDrawing(control);
                var trace = drawing.Children.OfType<GeometryDrawing>()
                    .Single(child => child.Geometry is StreamGeometry);
                var figures = PathFigureCollection.Parse(trace.Geometry.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(2, figures.Count);
                var firstEnd = ((LineSegment)figures[0].Segments[0]).Point;
                Assert.Equal(3 + .2 / 10 * 200, firstEnd.X, 4);
                Assert.Equal(3 + 1.4 / 10 * 200, figures[1].StartPoint.X, 4);
                var lastEnd = ((LineSegment)figures[1].Segments[0]).Point;
                Assert.Equal(83, lastEnd.X, 4);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
