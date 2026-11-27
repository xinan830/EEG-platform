using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

/// <summary>
/// VS-style dock host supporting adaptive auto-grid layout, drag-and-drop split/tab/float docking,
/// floating window dock-back, and JSON layout persistence.
/// </summary>
public sealed partial class MultiAnalysisDockHost : UserControl
{
    private const double WideTwoColumnMinWidth = 820d;
    private const double NarrowCardMinHeight = 300d;

    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(
        nameof(Waveform), typeof(FrameworkElement), typeof(MultiAnalysisDockHost), new PropertyMetadata(null, OnWaveformChanged));
    public static readonly DependencyProperty RightSlotsProperty = DependencyProperty.Register(
        nameof(RightSlots), typeof(IEnumerable), typeof(MultiAnalysisDockHost), new PropertyMetadata(null, OnRightSlotsChanged));
    public static readonly DependencyProperty BottomSlotsProperty = DependencyProperty.Register(
        nameof(BottomSlots), typeof(IEnumerable), typeof(MultiAnalysisDockHost), new PropertyMetadata(null, OnBottomSlotsChanged));
    public static readonly DependencyProperty FloatingSlotsProperty = DependencyProperty.Register(
        nameof(FloatingSlots), typeof(IEnumerable), typeof(MultiAnalysisDockHost), new PropertyMetadata(null, OnFloatingSlotsChanged));
    public static readonly DependencyProperty PanelTemplateProperty = DependencyProperty.Register(
        nameof(PanelTemplate), typeof(DataTemplate), typeof(MultiAnalysisDockHost), new PropertyMetadata(null, OnPanelTemplateChanged));
    public static readonly DependencyProperty FocusSlotCommandProperty = DependencyProperty.Register(
        nameof(FocusSlotCommand), typeof(ICommand), typeof(MultiAnalysisDockHost));
    public static readonly DependencyProperty FloatingWindowClosedCommandProperty = DependencyProperty.Register(
        nameof(FloatingWindowClosedCommand), typeof(ICommand), typeof(MultiAnalysisDockHost));
    public static readonly DependencyProperty FloatSlotCommandProperty = DependencyProperty.Register(
        nameof(FloatSlotCommand), typeof(ICommand), typeof(MultiAnalysisDockHost));
    public static readonly DependencyProperty DockFloatingSlotCommandProperty = DependencyProperty.Register(
        nameof(DockFloatingSlotCommand), typeof(ICommand), typeof(MultiAnalysisDockHost));
    public static readonly DependencyProperty EnableLayoutPersistenceProperty = DependencyProperty.Register(
        nameof(EnableLayoutPersistence), typeof(bool), typeof(MultiAnalysisDockHost), new PropertyMetadata(false, OnEnablePersistenceChanged));

    private readonly Grid rootContainer = new();
    private readonly Grid workspace = new();
    private readonly ColumnDefinition waveformColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition splitterColumn = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition rightColumn = new() { Width = new GridLength(0) };
    private readonly RowDefinition mainRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly RowDefinition horizontalSplitterRow = new() { Height = new GridLength(0) };
    private readonly RowDefinition bottomRow = new() { Height = new GridLength(0) };
    private readonly GridSplitter verticalSplitter = CreateSplitter(GridResizeDirection.Columns, "调整波形与右侧分析区宽度", 6);
    private readonly GridSplitter horizontalSplitter = CreateSplitter(GridResizeDirection.Rows, "调整波形与底部指标区高度", 6);

    private readonly Grid rightContainer = new() { Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)) };
    private readonly Border quickBar = new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(8, 5, 8, 5),
        Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel quickBarPills = new() { Orientation = Orientation.Horizontal };
    private readonly Grid rightSurface = new();
    private readonly ScrollViewer narrowScroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel narrowStack = new();
    private readonly Grid rightGrid = new();
    private readonly Grid bottomContainer = new();
    private readonly Grid customSurface = new() { Visibility = Visibility.Collapsed };

    private readonly Dictionary<object, DockCardChrome> rightCardHosts = [];
    private readonly Dictionary<object, DockCardChrome> bottomCardHosts = [];
    private readonly MultiAnalysisFloatingWindowManager floatingManager;
    private readonly DockTreeRenderer treeRenderer = new();
    private readonly DockDragCoordinator dragCoordinator;

    private DockCardChrome? waveformChrome;
    private DockNode? customLayoutTree;
    private bool isCustomLayout;
    private GridLength savedWaveformWidth = new(1, GridUnitType.Star);
    private GridLength savedRightWidth = new(1, GridUnitType.Star);
    private GridLength savedMainHeight = new(1, GridUnitType.Star);
    private GridLength savedBottomHeight = new(260, GridUnitType.Pixel);
    private bool rightWasVisible;
    private bool bottomWasVisible;
    private (int Count, bool Wide, object? Maximized)? lastRightLayoutSignature;

    public MultiAnalysisDockHost()
    {
        InitializeAutoWorkspaceGrid();
        rootContainer.Children.Add(workspace);
        rootContainer.Children.Add(customSurface);
        Content = rootContainer;

        dragCoordinator = new DockDragCoordinator(
            this,
            excludeId => treeRenderer.CollectTargetRegions(excludeId),
            HandleDropDock,
            HandleDropFloat);

        floatingManager = new MultiAnalysisFloatingWindowManager(
            () => Window.GetWindow(this),
            () => PanelTemplate,
            slot => { if (FloatingWindowClosedCommand?.CanExecute(slot) == true) FloatingWindowClosedCommand.Execute(slot); },
            (window, slot) => dragCoordinator.AttachFloatingWindow(window, ResolvePaneId(slot), ReadStringProperty(slot, "DisplayName") ?? "算法分析"));

        Unloaded += (_, _) =>
        {
            dragCoordinator.Dispose();
            floatingManager.CloseAll();
        };
        Loaded += (_, _) => SyncFloatingWindows();
        UpdateWorkspaceVisibility();
    }

    public FrameworkElement? Waveform { get => (FrameworkElement?)GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
    public IEnumerable? RightSlots { get => (IEnumerable?)GetValue(RightSlotsProperty); set => SetValue(RightSlotsProperty, value); }
    public IEnumerable? BottomSlots { get => (IEnumerable?)GetValue(BottomSlotsProperty); set => SetValue(BottomSlotsProperty, value); }
    public IEnumerable? FloatingSlots { get => (IEnumerable?)GetValue(FloatingSlotsProperty); set => SetValue(FloatingSlotsProperty, value); }
    public DataTemplate? PanelTemplate { get => (DataTemplate?)GetValue(PanelTemplateProperty); set => SetValue(PanelTemplateProperty, value); }
    public ICommand? FocusSlotCommand { get => (ICommand?)GetValue(FocusSlotCommandProperty); set => SetValue(FocusSlotCommandProperty, value); }
    public ICommand? FloatingWindowClosedCommand { get => (ICommand?)GetValue(FloatingWindowClosedCommandProperty); set => SetValue(FloatingWindowClosedCommandProperty, value); }
    public ICommand? FloatSlotCommand { get => (ICommand?)GetValue(FloatSlotCommandProperty); set => SetValue(FloatSlotCommandProperty, value); }
    public ICommand? DockFloatingSlotCommand { get => (ICommand?)GetValue(DockFloatingSlotCommandProperty); set => SetValue(DockFloatingSlotCommandProperty, value); }
    public bool EnableLayoutPersistence { get => (bool)GetValue(EnableLayoutPersistenceProperty); set => SetValue(EnableLayoutPersistenceProperty, value); }
    public bool IsCustomLayout => isCustomLayout;

    public void ActivateFloatingPanel(object slot) => floatingManager.Activate(slot);

    public void ApplyPreset(DockLayoutPreset preset)
    {
        if (preset == DockLayoutPreset.AutoClassic)
        {
            ResetToAutoLayout();
            return;
        }
        var analysisIds = Materialize(RightSlots).Concat(Materialize(BottomSlots)).Select(ResolvePaneId).ToList();
        customLayoutTree = DockTreeOperations.BuildPreset(preset, analysisIds);
        isCustomLayout = true;
        RenderActiveMode();
        SaveLayoutIfEnabled();
    }

    public void ResetToAutoLayout()
    {
        isCustomLayout = false;
        customLayoutTree = null;
        if (EnableLayoutPersistence) DockLayoutPersistence.Clear();
        RenderActiveMode();
    }

    internal void DockPaneAt(string paneId, string? targetPaneId, DockDropZone zone) =>
        HandleDropDock(paneId, targetPaneId, zone);

    private void InitializeAutoWorkspaceGrid()
    {
        workspace.ColumnDefinitions.Add(waveformColumn);
        workspace.ColumnDefinitions.Add(splitterColumn);
        workspace.ColumnDefinitions.Add(rightColumn);
        workspace.RowDefinitions.Add(mainRow);
        workspace.RowDefinitions.Add(horizontalSplitterRow);
        workspace.RowDefinitions.Add(bottomRow);
        verticalSplitter.SetResourceReference(BackgroundProperty, "AnalysisDividerBrush");
        horizontalSplitter.SetResourceReference(BackgroundProperty, "AnalysisDividerBrush");

        rightContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        quickBar.Child = quickBarPills;
        Grid.SetRow(quickBar, 0);
        Grid.SetRow(rightSurface, 1);
        rightContainer.Children.Add(quickBar);
        rightContainer.Children.Add(rightSurface);
        narrowScroller.Content = narrowStack;
        rightContainer.SizeChanged += (_, _) => { if (!isCustomLayout) UpdateRightLayoutIfNeeded(); };

        Grid.SetColumn(verticalSplitter, 1);
        Grid.SetRow(verticalSplitter, 0);
        Grid.SetRowSpan(verticalSplitter, 3);
        Grid.SetColumn(rightContainer, 2);
        Grid.SetRow(rightContainer, 0);
        Grid.SetRowSpan(rightContainer, 3);

        workspace.Children.Add(verticalSplitter);
        workspace.Children.Add(horizontalSplitter);
        workspace.Children.Add(rightContainer);
        workspace.Children.Add(bottomContainer);
    }

    private static void OnWaveformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MultiAnalysisDockHost)d;
        if (e.OldValue is FrameworkElement oldEl)
        {
            host.DetachFromParent(oldEl);
            host.waveformChrome?.DetachContent(oldEl);
            host.waveformChrome = null;
        }
        if (e.NewValue is FrameworkElement newEl)
        {
            host.waveformChrome = host.CreatePaneChrome(DockTreeOperations.WaveformPaneId, "脑电波形", directContent: newEl);
            host.RenderActiveMode();
        }
    }

    private static void OnRightSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: true, isFloating: false);
    private static void OnBottomSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: false, isFloating: false);
    private static void OnFloatingSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: false, isFloating: true);

    private static void OnEnablePersistenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MultiAnalysisDockHost)d;
        if (e.NewValue is true) host.TryRestoreSavedLayout();
    }

    private static void OnPanelTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MultiAnalysisDockHost)d;
        foreach (var card in host.rightCardHosts.Values.Concat(host.bottomCardHosts.Values))
            card.UpdateTemplate(host.PanelTemplate);
        host.floatingManager.UpdateTemplate(host.PanelTemplate);
    }

    private void RebindCollection(object? oldValue, object? newValue, bool isRight, bool isFloating)
    {
        if (oldValue is INotifyCollectionChanged oldNotify)
        {
            if (isFloating) oldNotify.CollectionChanged -= OnFloatingCollectionChanged;
            else if (isRight) oldNotify.CollectionChanged -= OnRightCollectionChanged;
            else oldNotify.CollectionChanged -= OnBottomCollectionChanged;
        }
        if (newValue is INotifyCollectionChanged newNotify)
        {
            if (isFloating) newNotify.CollectionChanged += OnFloatingCollectionChanged;
            else if (isRight) newNotify.CollectionChanged += OnRightCollectionChanged;
            else newNotify.CollectionChanged += OnBottomCollectionChanged;
        }
        if (isFloating) SyncFloatingWindows();
        else if (isRight) SyncDockedCollection(RightSlots, rightCardHosts);
        else SyncDockedCollection(BottomSlots, bottomCardHosts);
    }

    private void OnRightCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncDockedCollection(RightSlots, rightCardHosts);
    private void OnBottomCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncDockedCollection(BottomSlots, bottomCardHosts);
    private void OnFloatingCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncFloatingWindows();

    private void SyncDockedCollection(IEnumerable? source, Dictionary<object, DockCardChrome> map)
    {
        var slots = Materialize(source);
        foreach (var slot in slots)
        {
            if (map.ContainsKey(slot)) continue;
            if (slot is INotifyPropertyChanged notify) notify.PropertyChanged += OnSlotPropertyChanged;
            map[slot] = CreatePaneChrome(ResolvePaneId(slot), ReadStringProperty(slot, "DisplayName") ?? "算法", slotData: slot);
        }
        foreach (var stale in map.Keys.Except(slots).ToArray())
        {
            if (stale is INotifyPropertyChanged notify) notify.PropertyChanged -= OnSlotPropertyChanged;
            DetachFromParent(map[stale]);
            map.Remove(stale);
        }
        lastRightLayoutSignature = null;
        RenderActiveMode();
    }

    private void SyncFloatingWindows() => floatingManager.Sync(FloatingSlots);

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is null) return;
        if (!isCustomLayout && e.PropertyName is "IsMaximized")
        {
            lastRightLayoutSignature = null;
            UpdateRightLayoutIfNeeded();
        }
        else if (e.PropertyName is "IsFocused")
        {
            if (!isCustomLayout && rightCardHosts.TryGetValue(sender, out var card))
            {
                card.SetChrome(true, Materialize(RightSlots).Count > 1, ReadBoolProperty(sender, "IsFocused"));
                RebuildQuickBar(Materialize(RightSlots));
            }
        }
    }

    private void RenderActiveMode()
    {
        var anyAnalysisPanels = rightCardHosts.Count > 0 || bottomCardHosts.Count > 0;
        if (isCustomLayout && anyAnalysisPanels)
        {
            workspace.Visibility = Visibility.Collapsed;
            customSurface.Visibility = Visibility.Visible;
            RenderCustomTree();
            return;
        }

        customSurface.Children.Clear();
        treeRenderer.CleanupVisuals();
        customSurface.Visibility = Visibility.Collapsed;
        workspace.Visibility = Visibility.Visible;

        if (waveformChrome is not null)
        {
            if (!ReferenceEquals(waveformChrome.Parent, workspace))
            {
                DetachFromParent(waveformChrome);
                Grid.SetColumn(waveformChrome, 0);
                Grid.SetRow(waveformChrome, 0);
                workspace.Children.Insert(0, waveformChrome);
            }
            waveformChrome.SetChrome(showDragBar: anyAnalysisPanels, showCardBorder: false, isFocused: false);
        }

        RenderAutoBottomSlots();
        UpdateWorkspaceVisibility();
        UpdateRightLayoutIfNeeded();
    }

    private void RenderAutoBottomSlots()
    {
        bottomContainer.Children.Clear();
        foreach (var slot in Materialize(BottomSlots))
        {
            if (!bottomCardHosts.TryGetValue(slot, out var container)) continue;
            DetachFromParent(container);
            container.SetChrome(showDragBar: true, showCardBorder: false, isFocused: ReadBoolProperty(slot, "IsFocused"));
            bottomContainer.Children.Add(container);
        }
    }

    private void UpdateWorkspaceVisibility()
    {
        var rightVisible = rightCardHosts.Count > 0;
        var bottomVisible = bottomCardHosts.Count > 0;
        if (rightWasVisible && !rightVisible) { savedWaveformWidth = waveformColumn.Width; savedRightWidth = rightColumn.Width; }
        if (bottomWasVisible && !bottomVisible) { savedMainHeight = mainRow.Height; savedBottomHeight = bottomRow.Height; }
        rightWasVisible = rightVisible;
        bottomWasVisible = bottomVisible;

        waveformColumn.Width = rightVisible ? savedWaveformWidth : new GridLength(1, GridUnitType.Star);
        splitterColumn.Width = new GridLength(rightVisible ? 6 : 0);
        rightColumn.Width = rightVisible ? savedRightWidth : new GridLength(0);
        verticalSplitter.Visibility = rightVisible ? Visibility.Visible : Visibility.Collapsed;
        rightContainer.Visibility = rightVisible ? Visibility.Visible : Visibility.Collapsed;

        mainRow.Height = bottomVisible ? savedMainHeight : new GridLength(1, GridUnitType.Star);
        horizontalSplitterRow.Height = new GridLength(bottomVisible ? 6 : 0);
        bottomRow.Height = bottomVisible ? savedBottomHeight : new GridLength(0);
        horizontalSplitter.Visibility = bottomVisible ? Visibility.Visible : Visibility.Collapsed;
        bottomContainer.Visibility = bottomVisible ? Visibility.Visible : Visibility.Collapsed;

        var bottomSpan = rightVisible ? 1 : 3;
        Grid.SetRow(horizontalSplitter, 1); Grid.SetColumn(horizontalSplitter, 0); Grid.SetColumnSpan(horizontalSplitter, bottomSpan);
        Grid.SetRow(bottomContainer, 2); Grid.SetColumn(bottomContainer, 0); Grid.SetColumnSpan(bottomContainer, bottomSpan);
    }

    private void UpdateRightLayoutIfNeeded()
    {
        var slots = Materialize(RightSlots);
        if (slots.Count == 0) { quickBar.Visibility = Visibility.Collapsed; rightSurface.Children.Clear(); lastRightLayoutSignature = null; return; }

        var maximized = slots.FirstOrDefault(slot => ReadBoolProperty(slot, "IsMaximized"));
        var visibleSlots = maximized is not null ? [maximized] : slots;
        var availableWidth = rightContainer.ActualWidth > 0 ? rightContainer.ActualWidth : workspace.ActualWidth * 0.5;
        var isWide = availableWidth >= WideTwoColumnMinWidth;
        var signature = (slots.Count, isWide, maximized);
        if (lastRightLayoutSignature == signature) return;
        lastRightLayoutSignature = signature;

        RebuildQuickBar(slots);
        AutoAnalysisGridLayout.ArrangeRightSurface(
            rightSurface, rightGrid, narrowScroller, narrowStack, slots, visibleSlots, isWide, NarrowCardMinHeight,
            slot => rightCardHosts[slot], slot => ReadBoolProperty(slot, "IsFocused"));
    }

    private void RebuildQuickBar(IReadOnlyList<object> slots) =>
        AutoAnalysisGridLayout.RebuildQuickBar(
            quickBar, quickBarPills, slots,
            slot => ReadBoolProperty(slot, "IsFocused"),
            slot => ReadStringProperty(slot, "DisplayName"),
            slot =>
            {
                if (FocusSlotCommand?.CanExecute(slot) == true) FocusSlotCommand.Execute(slot);
                if (rightCardHosts.TryGetValue(slot, out var host)) host.BringIntoView();
            });

    private void DetachFromParent(FrameworkElement element) { if (element.Parent is Panel panel) panel.Children.Remove(element); }
    internal static IReadOnlyList<object> Materialize(IEnumerable? items) { var list = new List<object>(); if (items is null) return list; foreach (var item in items) if (item is not null) list.Add(item); return list; }
    private static bool ReadBoolProperty(object target, string propertyName) => target.GetType().GetProperty(propertyName)?.GetValue(target) as bool? == true;
    internal static string? ReadStringProperty(object target, string propertyName) => target.GetType().GetProperty(propertyName)?.GetValue(target) as string;
    private static string ResolvePaneId(object slot) => ReadStringProperty(slot, "AlgorithmId") ?? ReadStringProperty(slot, "DisplayName") ?? slot.GetHashCode().ToString();
    private static GridSplitter CreateSplitter(GridResizeDirection direction, string toolTip, double size) => new()
    {
        Width = direction == GridResizeDirection.Columns ? size : double.NaN,
        Height = direction == GridResizeDirection.Rows ? size : double.NaN,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = direction, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true, ToolTip = toolTip,
    };
}
