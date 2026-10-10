using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop.UiTests;

internal sealed class AcquisitionUiEvidence(AcquisitionUiFixture fixture, Window window)
{
    private readonly List<object> history = [];
    private readonly HashSet<string> captured = [];
    private readonly List<string> failures = [];
    private bool sawPause;

    public void Capture()
    {
        var acquisition = fixture.Workspace.Acquisition;
        var state = fixture.Runtime.State.State;
        if (state == AcquisitionState.Paused) sawPause = true;
        var phase = state == AcquisitionState.Recording && sawPause ? "Resumed" : state.ToString();
        var source = acquisition.LiveMonitor.GetWaveformSource() as LiveWaveformSource;
        var frame = source is null ? null : WaveformDisplayFrameBuilder.Build(source, 1, 10000);
        var surfaces = Descendants(window).OfType<SciChartSurface>().ToArray();
        var labels = Descendants(window).OfType<TextBlock>().Where(text => text.IsVisible)
            .Select(text => text.Text).Where(text => text.EndsWith("-REF", StringComparison.Ordinal)).Distinct().ToArray();
        var series = surfaces.SelectMany(surface => surface.RenderableSeries)
            .Select(series => new { count = series.DataSeries?.Count ?? 0, name = series.DataSeries?.SeriesName }).ToArray();
        var mapping = acquisition.ChannelMapping.GetCurrentMapping();
        var snapshot = new
        {
            synthetic = true, filter = "identity transport; not filter mathematics", phase,
            selectedMontage = acquisition.SelectedMontageProfile?.Id,
            mapping, labels, series, latestCounter = fixture.Runtime.LatestSampleCounter,
            recordingDirectory = fixture.Runtime.RecordingDirectory,
            operation = acquisition.OperationMessage,
            traces = frame?.Traces.Select(trace => new { trace.Label, count = trace.Points.Count,
                minV = trace.Points.FirstOrDefault()?.MinVolts, maxV = trace.Points.FirstOrDefault()?.MaxVolts }).ToArray(),
            failures = failures.ToArray(),
        };
        File.WriteAllText(Path.Combine(fixture.Root, "current.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        if (captured.Contains(phase)) return;
        if (state is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused)
        {
            if (frame is null || frame.Traces.Count == 0 || series.Length == 0 || series.Any(item => item.count == 0)) return;
            Check(mapping.GetValueOrDefault(3) == "F88" && mapping.GetValueOrDefault(5) == "F3", phase, "Native mapping changed");
            var expected = new[] { "Fp1-REF", "Fp2-REF", "F88-REF", "F3-REF" };
            Check(frame.Traces.Select(trace => trace.Label).SequenceEqual(expected), phase, "Trace order/labels differ");
            Check(expected.All(labels.Contains), phase, "Rendered labels missing");
            foreach (var (label, native, baseline) in new[] { ("F88-REF", 3, 40e-6), ("F3-REF", 5, 60e-6) })
            {
                var trace = frame.Traces.SingleOrDefault(trace => trace.Label == label);
                Check(trace is not null && trace.Points.All(point => point.MinVolts >= baseline - 4.00001e-6 && point.MaxVolts <= baseline + 4.00001e-6), phase, $"{label} source mismatch");
                var batch = source!.Batches[0];
                var streamIndex = source.Metadata.Channels.Single(channel => channel.NativeChannelIndex == native).StreamIndex;
                Check(Math.Abs(batch.SampleMajorValues[streamIndex] - SyntheticDriver.Value(native, batch.FirstSampleCounter)) < 1e-12, phase, $"CH{native} sample mismatch");
            }
            Check(series.Length == 4, phase, "Expected four real SciChart series");
        }
        captured.Add(phase);
        history.Add(snapshot);
        File.WriteAllText(Path.Combine(fixture.Root, "history.json"), JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Check(bool condition, string phase, string message)
    {
        if (!condition) failures.Add($"{phase}: {message}");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
