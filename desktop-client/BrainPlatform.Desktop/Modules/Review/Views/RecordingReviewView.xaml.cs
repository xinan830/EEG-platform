using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Views;

public partial class RecordingReviewView : UserControl
{
    private readonly DispatcherTimer playbackTimer;
    private bool updatingControls;

    public RecordingReviewView()
    {
        InitializeComponent();
        playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private RecordingReviewViewModel? ViewModel => DataContext as RecordingReviewViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        playbackTimer.Tick += OnPlaybackTimerTick;
        playbackTimer.Start();
        UpdateControls();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        playbackTimer.Stop();
        playbackTimer.Tick -= OnPlaybackTimerTick;
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RecordingReviewViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is RecordingReviewViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateControls();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordingReviewViewModel.PositionSeconds)
            or nameof(RecordingReviewViewModel.DurationSeconds)
            or nameof(RecordingReviewViewModel.VisibleDurationSeconds)
            or nameof(RecordingReviewViewModel.ViewportStartSeconds)
            or nameof(RecordingReviewViewModel.IsPlaying)
            or nameof(RecordingReviewViewModel.IsCompleted))
        {
            UpdateControls();
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (ViewModel is not { IsPlaying: true } viewModel)
        {
            return;
        }

        viewModel.TickPlayback();
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TogglePlayback();
        UpdateControls();
    }

    private void OnPlaybackSpeedClick(object sender, RoutedEventArgs e) => ViewModel?.CyclePlaybackSpeed();

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

    private async void OnTimelinePositionCommitted(object? sender, double positionSeconds)
    {
        if (ViewModel is not null)
        {
            await ViewModel.NavigateFromTrackClickAsync(positionSeconds);
        }
    }

    private async void OnMontageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || ViewModel is null || sender is not ComboBox selector ||
            selector.SelectedItem is not MontageProfile profile)
        {
            return;
        }

        await ViewModel.SelectViewingMontageAsync(profile);
    }

    private async void OnSeekToEventClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedRecordingEvent is { } item)
        {
            await ViewModel.SeekToEventAsync(item);
        }
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

    private void UpdateControls()
    {
        if (ViewModel is null || !IsLoaded)
        {
            return;
        }

        updatingControls = true;
        try
        {
            MontageSelector.SelectedItem = ViewModel.CurrentViewingMontage;
        }
        finally
        {
            updatingControls = false;
        }
    }

}
