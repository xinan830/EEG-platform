using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class DockTreeLayoutTests
{
    [Fact]
    public void DockTree_supports_splits_tab_merging_outer_edges_and_json_roundtrip()
    {
        var tree = DockTreeOperations.BuildFromAutoLayout(["rbp", "psd"], [], wideRight: true);
        tree = DockTreeOperations.InsertPane(tree, "psd", "rbp", DockDropZone.CenterTab);
        tree = DockTreeOperations.InsertPane(tree, "stft", DockTreeOperations.WaveformPaneId, DockDropZone.Top, 0.35);
        tree = DockTreeOperations.InsertPane(tree, "tnk_biofeedback", null, DockDropZone.OuterBottom, 0.3);

        var snapshot = new DockLayoutSnapshot
        {
            IsCustomLayout = true,
            Root = tree,
            FloatingPaneIds = ["faa"],
        };
        var json = DockTreeOperations.SerializeSnapshot(snapshot);
        var restored = DockTreeOperations.DeserializeSnapshot(json);

        Assert.NotNull(restored);
        Assert.True(restored!.IsCustomLayout);
        Assert.Contains("faa", restored.FloatingPaneIds);
        Assert.True(DockTreeOperations.ContainsPane(restored.Root, "rbp"));
        Assert.True(DockTreeOperations.ContainsPane(restored.Root, "psd"));
        Assert.True(DockTreeOperations.ContainsPane(restored.Root, "stft"));
        Assert.True(DockTreeOperations.ContainsPane(restored.Root, "tnk_biofeedback"));
        Assert.True(DockTreeOperations.ContainsPane(restored.Root, DockTreeOperations.WaveformPaneId));

        // Removing one pane from a 2-tab group collapses it cleanly back to a single pane.
        var removed = DockTreeOperations.RemovePane(restored.Root, "psd");
        Assert.False(DockTreeOperations.ContainsPane(removed, "psd"));
        Assert.True(DockTreeOperations.ContainsPane(removed, "rbp"));
    }

    [Fact]
    public void DockLayoutPersistence_saves_loads_and_clears_file_atomically()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dock-layout-test-{Guid.NewGuid():N}.json");
        try
        {
            var tree = DockTreeOperations.BuildPreset(DockLayoutPreset.TopBottomWaveform, ["rbp", "psd"]);
            DockLayoutPersistence.Save(new DockLayoutSnapshot { IsCustomLayout = true, Root = tree }, tempFile);
            var loaded = DockLayoutPersistence.Load(tempFile);

            Assert.NotNull(loaded);
            Assert.True(loaded!.IsCustomLayout);
            Assert.True(DockTreeOperations.ContainsPane(loaded.Root, "rbp"));
            Assert.True(DockTreeOperations.ContainsPane(loaded.Root, "psd"));

            DockLayoutPersistence.Clear(tempFile);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MultiAnalysisDockHost_preserves_pane_instances_across_tab_merge_and_presets() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var right = new ObservableCollection<FakeSlot> { new("rbp", "RBP"), new("psd", "PSD") };
            var bottom = new ObservableCollection<FakeSlot>();
            var floating = new ObservableCollection<FakeSlot>();
            var waveform = new Border();

            var host = new MultiAnalysisDockHost
            {
                Waveform = waveform,
                RightSlots = right,
                BottomSlots = bottom,
                FloatingSlots = floating,
                PanelTemplate = new DataTemplate(),
            };

            host.Measure(new Size(1600, 900));
            host.Arrange(new Rect(0, 0, 1600, 900));
            host.UpdateLayout();

            Assert.False(host.IsCustomLayout);

            // Merge PSD into RBP as a tab group -> both stay mounted inside DockTabGroupView via Visibility switching.
            host.DockPaneAt("psd", "rbp", DockDropZone.CenterTab);
            host.Measure(new Size(1600, 900));
            host.Arrange(new Rect(0, 0, 1600, 900));
            host.UpdateLayout();

            Assert.True(host.IsCustomLayout);
            var tabGroup = FindDescendant<DockTabGroupView>(host);
            Assert.NotNull(tabGroup);
            Assert.Equal("psd", tabGroup!.ActivePaneId);

            var chromesInTabs = CountDescendants<DockCardChrome>(tabGroup);
            Assert.Equal(2, chromesInTabs);
            tabGroup.ActivateTab("rbp");
            Assert.Equal("rbp", tabGroup.ActivePaneId);
            Assert.Equal(2, CountDescendants<DockCardChrome>(tabGroup));

            // Switch to TopBottomWaveform preset -> both cards stay alive without losing Waveform.
            host.ApplyPreset(DockLayoutPreset.TopBottomWaveform);
            Assert.True(host.IsCustomLayout);
            Assert.Null(FindDescendant<DockTabGroupView>(host));
            Assert.Equal(3, CountDescendants<DockCardChrome>(host)); // 2 analysis cards + 1 waveform chrome

            // Reset back to responsive auto-grid layout.
            host.ResetToAutoLayout();
            Assert.False(host.IsCustomLayout);
        });

    [Fact]
    public void DockCardChrome_merges_drag_handle_into_AnalysisPanelShell_single_header() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var template = new DataTemplate();
            var shellFactory = new FrameworkElementFactory(typeof(AnalysisPanelShell));
            shellFactory.SetValue(AnalysisPanelShell.TitleProperty, "RBP");
            template.VisualTree = shellFactory;

            var chrome = new DockCardChrome("rbp", "RBP 相对频段功率", template: template, slotData: new FakeSlot("rbp", "RBP"));
            chrome.SetChrome(showDragBar: true, showCardBorder: true, isFocused: true);
            chrome.Measure(new Size(600, 400));
            chrome.Arrange(new Rect(0, 0, 600, 400));
            chrome.UpdateLayout();

            // Separate dragStrip stays collapsed for AnalysisPanelShell-hosted cards; AnalysisPanelShell inherits IsDockDragEnabled.
            var dragStrip = typeof(DockCardChrome).GetField("dragStrip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(chrome) as Border;
            Assert.NotNull(dragStrip);
            Assert.Equal(Visibility.Collapsed, dragStrip!.Visibility);

            var shell = FindDescendant<AnalysisPanelShell>(chrome);
            Assert.NotNull(shell);
            Assert.True(shell!.IsDockDragEnabled);

            var dragStartedCount = 0;
            chrome.DragStarted += (_, _, _) => dragStartedCount++;
            shell.RaiseEvent(new RoutedEventArgs(AnalysisPanelShell.HeaderDragStartedEvent, shell));
            Assert.Equal(1, dragStartedCount);
        });

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = 0;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T) count++;
            count += CountDescendants<T>(child);
        }
        return count;
    }

    private sealed class FakeSlot(string algorithmId, string displayName) : ObservableObject
    {
        private bool isFocused;
        public string AlgorithmId { get; } = algorithmId;
        public string DisplayName { get; } = displayName;
        public bool IsFocused { get => isFocused; set => SetProperty(ref isFocused, value); }
    }
}
