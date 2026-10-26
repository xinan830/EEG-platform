using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

public partial class TnkDetailView : UserControl
{
    private TnkDetailViewModel? active;
    public TnkDetailView()
    {
        InitializeComponent();
        Loaded += (_, _) => Activate();
        Unloaded += (_, _) => { active?.DeactivateWorkbench(); active = null; };
        DataContextChanged += (_, _) => { if (IsLoaded) Activate(); };
        WaveformView.SeekRequested += (_, seconds) => active?.SeekFromChart(seconds);
        WaveformPositionSlider.PreviewMouseLeftButtonDown += (_, _) => active?.PauseForWaveformSeek();
        WaveformPositionSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (active is not null) active.SeekFromChart(active.WaveformPosition);
        };
    }

    private void OnPlotLoaded(object sender, System.Windows.RoutedEventArgs args) => ((TnkAnalysisPlotView)sender).SeekRequested += OnPlotSeek;
    private void OnPlotUnloaded(object sender, System.Windows.RoutedEventArgs args) => ((TnkAnalysisPlotView)sender).SeekRequested -= OnPlotSeek;
    private void OnPlotSeek(object? sender, double seconds) => active?.SeekFromChart(seconds);

    private void Activate()
    {
        active?.DeactivateWorkbench();
        active = DataContext as TnkDetailViewModel;
        active?.ActivateWorkbench();
    }
}
