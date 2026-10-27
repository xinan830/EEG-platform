using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Events.Views;
using BrainPlatform.Desktop.Modules.Review.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public partial class RecordingReviewView : UserControl
{
    private readonly DispatcherTimer playbackTimer;
    private readonly ReviewPlaybackTickDiagnostics playbackTickDiagnostics = new();
    private ReviewResourceMonitor? resourceMonitor;

    public RecordingReviewView()
    {
        InitializeComponent();
        playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty IsDataInfoOpenProperty =
        DependencyProperty.Register(
            nameof(IsDataInfoOpen),
            typeof(bool),
            typeof(RecordingReviewView),
            new PropertyMetadata(false));

    public bool IsDataInfoOpen
    {
        get => (bool)GetValue(IsDataInfoOpenProperty);
        set => SetValue(IsDataInfoOpenProperty, value);
    }

    private RecordingReviewViewModel? ViewModel => DataContext as RecordingReviewViewModel;

    private void OnToggleDataInfoClick(object sender, RoutedEventArgs e)
    {
        IsDataInfoOpen = !IsDataInfoOpen;
    }

    private void OnCloseDataInfoClick(object sender, RoutedEventArgs e)
    {
        IsDataInfoOpen = false;
    }

    private void OnAnalysisMenuOpened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && ViewModel?.LoadAnalysisSettingsCommand is { } command && command.CanExecute(null))
            command.Execute(null);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        var definition = EventShortcutInput.Match(
            viewModel.EnabledEventDefinitions, ShortcutScope.Review, e, out var shortcut);
        if (definition is null) return;

        e.Handled = true;
        try
        {
            await viewModel.CreateKeyboardEventAsync(definition.Id, shortcut);
        }
        catch (Exception exception)
        {
            viewModel.ReportEventShortcutError(exception);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        playbackTimer.Tick += OnPlaybackTimerTick;
        playbackTimer.Start();
        if (ReviewPerformanceDiagnostics.Enabled && resourceMonitor is null)
        {
            resourceMonitor = new ReviewResourceMonitor(
                action =>
                {
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return false;
                    try
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        // Shutdown can race the state check; a diagnostic probe must not report a user error.
                        return false;
                    }
                },
                () => ViewModel is { CurrentFrame: { } frame } viewModel
                    ? new ReviewResourceContext(viewModel.ReviewSessionId, frame.RecordingSessionId,
                        viewModel.PositionSeconds, viewModel.IsPlaying)
                    : null);
            resourceMonitor.Start();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        resourceMonitor?.Dispose();
        resourceMonitor = null;
        ViewModel?.EndTimelineDrag();
        playbackTimer.Stop();
        playbackTimer.Tick -= OnPlaybackTimerTick;
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (ViewModel is not { IsPlaying: true } viewModel)
        {
            playbackTickDiagnostics.Reset();
            return;
        }

        viewModel.TickPlayback();
        playbackTickDiagnostics.Record(viewModel.RecordingId, viewModel.SamplingRateHz,
            viewModel.PlaybackSpeedText, started, System.Diagnostics.Stopwatch.GetTimestamp(),
            viewModel.PositionSeconds);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TogglePlayback();
    }

    private void OnPlaybackSpeedClick(object sender, RoutedEventArgs e) => ViewModel?.CyclePlaybackSpeed();

    private void OnPointLineModeClick(object sender, RoutedEventArgs e)
    {
        ReviewWaveformCanvas.IsPointLineModeEnabled = !ReviewWaveformCanvas.IsPointLineModeEnabled;
        if (ReviewWaveformCanvas.IsPointLineModeEnabled)
            PointLineModeButton.Style = (Style)FindResource("EegActiveToolButtonStyle");
        else
            PointLineModeButton.ClearValue(StyleProperty);
    }

    private async void OnNavigateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: string tag })
        {
            return;
        }

        if (string.Equals(tag, "page-back", StringComparison.Ordinal))
        {
            await ViewModel.SeekPageAsync(-1);
            return;
        }

        if (string.Equals(tag, "page-forward", StringComparison.Ordinal))
        {
            await ViewModel.SeekPageAsync(1);
            return;
        }

        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return;
        }

        await ViewModel.SeekRelativeAsync(seconds);
    }

    private void OnTimelinePreviewPositionChanged(object? sender, double positionSeconds)
    {
        ViewModel?.PreviewSeek(positionSeconds);
    }

    private void OnTimelineDragStarted(object? sender, EventArgs e) => ViewModel?.BeginTimelineDrag();

    private void OnTimelineDragCompleted(object? sender, EventArgs e) => ViewModel?.EndTimelineDrag();

    private async void OnTimelinePositionCommitted(object? sender, double positionSeconds)
    {
        if (ViewModel is not null)
        {
            await ViewModel.NavigateFromTrackClickAsync(positionSeconds);
        }
    }

    private async void OnSeekToEventClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedRecordingEvent is { } item)
        {
            await ViewModel.SeekToEventAsync(item);
        }
    }

    private async void OnMontageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not ComboBox { SelectedItem: MontageProfile profile } ||
            ReferenceEquals(profile, viewModel.CurrentViewingMontage)) return;

        try { await viewModel.SelectViewingMontageAsync(profile); }
        catch (Exception exception) { viewModel.ReportMontageSelectionError(exception); }
    }

    private void OnPaperSpeedSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyScaleSelection(sender, value => ViewModel!.PaperSpeedMillimetersPerSecond = value);

    private void OnTimebaseSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyScaleSelection(sender, value => ViewModel!.TimebaseSecondsPerScreen = value);

    private void ApplyScaleSelection(object sender, Action<double> apply)
    {
        if (ViewModel is null || sender is not ComboBox { SelectedValue: not null } comboBox)
        {
            return;
        }

        if (double.TryParse(
                comboBox.SelectedValue.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            apply(value);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as EegSessionWindow)?.Close();
    }

}
