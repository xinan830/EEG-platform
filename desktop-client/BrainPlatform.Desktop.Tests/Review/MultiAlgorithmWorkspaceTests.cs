using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Shared.Controls.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Multiple_algorithms_open_together_route_regions_and_fan_out_cursor_updates()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        var rbp = new PanelProvider("rbp");
        var psd = new PanelProvider("psd");
        var faa = new PanelProvider("faa");
        var iapf = new PanelProvider("iapf");
        var thetaBeta = new PanelProvider("theta_beta");
        var tnk = new PanelProvider("tnk_biofeedback");
        var manifest = Manifest(configuration);
        var recorded = new RegisteredRecording(manifest.SessionId.ToString("N"), "fixture", ".wpf", "now", 500, 100,
            ["F3", "F4", "Fz", "Pz", "F7"], "sha");
        await using var vm = new RecordingReviewViewModel(new FakeReader(manifest, 100), catalog,
            recordingDirectory: "_temp/multi-algorithm-fixture",
            analysisCoordinators:
            [
                new ReviewAnalysisCoordinator(rbp),
                new ReviewAnalysisCoordinator(psd),
                new ReviewAnalysisCoordinator(faa),
                new ReviewAnalysisCoordinator(iapf),
                new ReviewAnalysisCoordinator(thetaBeta),
                new ReviewAnalysisCoordinator(tnk),
            ],
            registeredAnalysisRecording: recorded);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);

        await vm.OpenAnalysisAsync("rbp");
        await vm.OpenAnalysisAsync("psd");
        await vm.OpenAnalysisAsync("faa");
        await vm.OpenAnalysisAsync("iapf");
        await vm.OpenAnalysisAsync("tnk_biofeedback");

        Assert.Equal(5, vm.OpenAnalysisSlots.Count);
        Assert.Equal(4, vm.RightDockedAnalysisSlots.Count);
        Assert.Single(vm.BottomDockedAnalysisSlots);
        Assert.Empty(vm.FloatingAnalysisSlots);
        Assert.Equal("tnk_biofeedback", vm.BottomDockedAnalysisSlots[0].AlgorithmId);
        Assert.True(vm.IsRbpSlotOpen);
        Assert.True(vm.IsPsdSlotOpen);
        Assert.True(vm.IsFaaSlotOpen);
        Assert.True(vm.IsIapfSlotOpen);
        Assert.True(vm.IsTnkSlotOpen);

        // Opening a 5th right-side algorithm auto-floats instead of crowding the 2x2 dock.
        await vm.OpenAnalysisAsync("theta_beta");
        Assert.Equal(6, vm.OpenAnalysisSlots.Count);
        Assert.Equal(4, vm.RightDockedAnalysisSlots.Count);
        var floated = Assert.Single(vm.FloatingAnalysisSlots);
        Assert.Equal("theta_beta", floated.AlgorithmId);
        Assert.True(floated.IsFloating);

        // Docking back is blocked while 4 panels are docked; closing one opens a dock slot.
        vm.ToggleFloatingSlot("theta_beta");
        Assert.True(floated.IsFloating);
        await vm.CloseAnalysisAsync("psd");
        Assert.Equal(3, vm.RightDockedAnalysisSlots.Count);
        Assert.False(vm.IsPsdSlotOpen);

        vm.ToggleFloatingSlot("theta_beta");
        Assert.False(floated.IsFloating);
        Assert.Empty(vm.FloatingAnalysisSlots);
        Assert.Equal(4, vm.RightDockedAnalysisSlots.Count);

        // Seeking fans out to all currently open algorithms.
        var rbpBeforeSeek = rbp.Loads;
        var faaBeforeSeek = faa.Loads;
        var thetaBeforeSeek = thetaBeta.Loads;
        await vm.SeekAsync(35);
        await Task.Delay(60);
        Assert.True(rbp.Loads > rbpBeforeSeek);
        Assert.True(faa.Loads > faaBeforeSeek);
        Assert.True(thetaBeta.Loads > thetaBeforeSeek);
    }

    [Fact]
    public async Task Brainbeat_variants_and_parameter_edits_remain_isolated_across_open_panels()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F4", "Fz", "Pz"]), [], []);
        var rbp = new PanelProvider("rbp");
        var thetaBeta = new PanelProvider("theta_beta");
        var classic = new PanelProvider("brainbeat");
        var individualized = new PanelProvider("brainbeat_individualized");
        var manifest = Manifest(configuration);
        var recorded = new RegisteredRecording(manifest.SessionId.ToString("N"), "fixture", ".wpf", "now", 500, 100,
            ["F3", "F4", "Fz", "Pz", "F7"], "sha");
        await using var vm = new RecordingReviewViewModel(new FakeReader(manifest, 100), catalog,
            recordingDirectory: "_temp/isolated-multi-fixture",
            analysisCoordinators:
            [
                new ReviewAnalysisCoordinator(rbp),
                new ReviewAnalysisCoordinator(thetaBeta),
                new ReviewAnalysisCoordinator(classic),
                new ReviewAnalysisCoordinator(individualized),
            ],
            registeredAnalysisRecording: recorded);
        await vm.InitializeAsync();
        await vm.SeekAsync(25);

        await vm.OpenAnalysisAsync("rbp");
        await vm.OpenAnalysisAsync("theta_beta");
        await vm.OpenAnalysisAsync("brainbeat");
        await vm.OpenAnalysisAsync("brainbeat_individualized");

        var classicSlot = vm.OpenAnalysisSlots.Single(s => s.AlgorithmId == "brainbeat");
        var indivSlot = vm.OpenAnalysisSlots.Single(s => s.AlgorithmId == "brainbeat_individualized");
        Assert.NotSame(classicSlot.ActiveScalarParameters, indivSlot.ActiveScalarParameters);
        Assert.NotSame(classicSlot.ActiveScalarAnalysis, indivSlot.ActiveScalarAnalysis);
        Assert.Same(vm.BrainbeatAnalysis, classicSlot.ActiveScalarAnalysis);
        Assert.Same(vm.IndividualizedBrainbeatAnalysis, indivSlot.ActiveScalarAnalysis);

        var rbpLoadsBefore = rbp.Loads;
        var thetaUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thetaSlot = vm.OpenAnalysisSlots.Single(s => s.AlgorithmId == "theta_beta");
        thetaSlot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReviewAnalysisCardViewModel.CurrentWindow) && thetaBeta.Configurations.Count == 2)
                thetaUpdated.TrySetResult();
        };
        vm.ThetaBetaParameters.FrequencyMode = "individualized";
        await thetaUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(rbpLoadsBefore, rbp.Loads);

        // Maximize toggles cleanly and focuses other cards out of maximize mode.
        vm.ToggleMaximizeSlot("rbp");
        Assert.True(vm.IsAnyRightPanelMaximized);
        Assert.Equal("rbp", vm.MaximizedRightAnalysisSlot?.AlgorithmId);
        vm.FocusAnalysisSlot("theta_beta");
        Assert.False(vm.IsAnyRightPanelMaximized);
        Assert.Equal("theta_beta", vm.ActiveAnalysisSlot?.AlgorithmId);
    }

    [Fact]
    public void Multi_analysis_dock_host_adapts_wide_grid_and_narrow_scroll_automatically() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var right = new ObservableCollection<FakeSlot>();
            var bottom = new ObservableCollection<FakeSlot>();
            var floating = new ObservableCollection<FakeSlot>();
            var host = new MultiAnalysisDockHost
            {
                Waveform = new Border { Background = System.Windows.Media.Brushes.White },
                RightSlots = right,
                BottomSlots = bottom,
                FloatingSlots = floating,
                PanelTemplate = new DataTemplate(),
            };

            right.Add(new FakeSlot("RBP"));
            right.Add(new FakeSlot("PSD"));
            right.Add(new FakeSlot("FAA"));
            host.Measure(new Size(2200, 900));
            host.Arrange(new Rect(0, 0, 2200, 900));
            host.UpdateLayout();

            var surface = FindDescendant<Grid>(host, g => g.Children.Count > 0 && ReferenceEquals(g.Children[0].GetType(), typeof(Grid)) && g.RowDefinitions.Count == 0);
            var rightGrid = typeof(MultiAnalysisDockHost).GetField("rightGrid",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host) as Grid;
            var rightSurface = typeof(MultiAnalysisDockHost).GetField("rightSurface",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host) as Grid;
            Assert.NotNull(rightGrid);
            Assert.NotNull(rightSurface);
            Assert.Contains(rightGrid!, rightSurface!.Children.OfType<UIElement>());
            Assert.Equal(2, rightGrid!.ColumnDefinitions.Count);
            Assert.Equal(2, rightGrid.RowDefinitions.Count);
            Assert.Equal(2, Grid.GetColumnSpan(rightGrid.Children[2]));

            // Resize narrower below 820 DIP right width -> automatically switches to ScrollViewer stack.
            host.Measure(new Size(1100, 900));
            host.Arrange(new Rect(0, 0, 1100, 900));
            host.UpdateLayout();
            var scroller = typeof(MultiAnalysisDockHost).GetField("narrowScroller",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host) as ScrollViewer;
            Assert.Contains(scroller!, rightSurface.Children.OfType<UIElement>());
        });

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            if (FindDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private sealed class FakeSlot(string displayName) : ObservableObject
    {
        private bool isMaximized;
        private bool isFocused;
        public string DisplayName { get; } = displayName;
        public bool IsMaximized { get => isMaximized; set => SetProperty(ref isMaximized, value); }
        public bool IsFocused { get => isFocused; set => SetProperty(ref isFocused, value); }
    }
}
