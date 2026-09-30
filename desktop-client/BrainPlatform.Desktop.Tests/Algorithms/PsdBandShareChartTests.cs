using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Psd;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class PsdBandShareChartTests
{
    [Fact]
    public void ChartSelection_SwitchesBetweenSpectrumAndBandShare()
    {
        var catalog = new AlgorithmListViewModel(new UnusedAlgorithmClient(), new OperationNotificationCenter());
        var detail = new PsdDetailViewModel(catalog);

        Assert.True(detail.IsSpectrumSelected);
        Assert.False(detail.IsBandShareSelected);

        detail.SelectedChart = detail.ChartOptions.Single(option => option.Kind == PsdChartKind.BandShare);
        Assert.False(detail.IsSpectrumSelected);
        Assert.True(detail.IsBandShareSelected);
    }

    [Fact]
    public void BandShare_UsesAvailableBackendValuesAndClearsStaleSeries()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var points = new ObservableCollection<AlgorithmListViewModel.PsdBandSharePoint>
                {
                    new("δ", "0.5–4 Hz", 0.25, "25%", "#2563EB"),
                    new("θ", "4–8 Hz", double.NaN, "不可用", "#059669", false),
                    new("α", "8–13 Hz", 0.75, "75%", "#DC2626"),
                };
                var chart = new PsdBandShareChart { Points = points };
                var surface = Assert.IsType<SciChartSurface>(chart.FindName("Surface"));
                var series = Assert.IsType<FastColumnRenderableSeries>(Assert.Single(surface.RenderableSeries));
                var values = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);

                Assert.Equal(2, values.Count);
                Assert.Equal(0, values.XValues[0]);
                Assert.Equal(2, values.XValues[1]);
                Assert.Equal(0.25, values.YValues[0]);
                Assert.Equal(0.75, values.YValues[1]);
                Assert.Equal("θ", surface.XAxes[0].LabelProvider.FormatLabel(1));
                Assert.Equal("不可用：θ", Assert.IsType<TextBlock>(chart.FindName("UnavailableBands")).Text);

                points.Add(new("β", "13–30 Hz", 0.5, "50%", "#D97706"));
                series = Assert.IsType<FastColumnRenderableSeries>(Assert.Single(surface.RenderableSeries));
                values = Assert.IsType<XyDataSeries<double, double>>(series.DataSeries);
                Assert.Equal(3, values.Count);
                Assert.Equal(3, values.XValues[2]);

                points.Clear();
                Assert.Empty(surface.RenderableSeries);
                Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(chart.FindName("EmptyState")).Visibility);
                Assert.Equal("", Assert.IsType<TextBlock>(chart.FindName("UnavailableBands")).Text);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class UnusedAlgorithmClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
