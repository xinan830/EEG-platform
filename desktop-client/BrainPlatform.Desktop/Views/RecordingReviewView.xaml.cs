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
    private bool tickInProgress;

    public RecordingReviewView()
    {
        InitializeComponent();
        playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50),
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
            or nameof(RecordingReviewViewModel.IsPlaying)
            or nameof(RecordingReviewViewModel.IsCompleted))
        {
            UpdateControls();
        }
    }

    private async void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (tickInProgress || ViewModel is not { IsPlaying: true } viewModel)
        {
            return;
        }

        tickInProgress = true;
        try
        {
            await viewModel.TickPlaybackAsync();
        }
        finally
        {
            tickInProgress = false;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TogglePlayback();
        UpdateControls();
    }

    private async void OnPositionReleased(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || updatingControls || sender is not Slider slider)
        {
            return;
        }

        await ViewModel.SeekAsync(slider.Value);
    }

    private async void OnDurationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || ViewModel is null || sender is not ComboBox selector ||
            selector.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), out var duration))
        {
            return;
        }

        await ViewModel.SetVisibleDurationAsync(duration);
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

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowProjectListView();
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
            PositionSlider.Maximum = Math.Max(0, ViewModel.DurationSeconds - ViewModel.VisibleDurationSeconds);
            PositionSlider.Value = Math.Clamp(ViewModel.PositionSeconds, 0, PositionSlider.Maximum);
            PositionText.Text = $"{ViewModel.PositionSeconds:0.0} / {ViewModel.DurationSeconds:0.0} s";
            DurationSelector.SelectedItem = DurationSelector.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => double.TryParse(item.Tag?.ToString(), out var duration)
                    && Math.Abs(duration - ViewModel.VisibleDurationSeconds) < 0.001);
            MontageSelector.SelectedItem = ViewModel.CurrentViewingMontage;
        }
        finally
        {
            updatingControls = false;
        }
    }

}
