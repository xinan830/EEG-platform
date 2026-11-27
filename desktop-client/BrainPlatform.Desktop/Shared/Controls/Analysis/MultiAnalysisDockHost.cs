using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

/// <summary>Auto-arranging multi-panel host: left waveform, adaptive right grid/stack, wide bottom dock, and floating windows.</summary>
public sealed class MultiAnalysisDockHost : UserControl
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

    private readonly Grid workspace = new();
    private readonly ColumnDefinition waveformColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition splitterColumn = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition rightColumn = new() { Width = new GridLength(0) };
    private readonly RowDefinition mainRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly RowDefinition horizontalSplitterRow = new() { Height = new GridLength(0) };
    private readonly RowDefinition bottomRow = new() { Height = new GridLength(0) };
    private readonly GridSplitter verticalSplitter = new()
    {
        Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = true, ToolTip = "调整波形与右侧分析区宽度",
    };
    private readonly GridSplitter horizontalSplitter = new()
    {
        Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = true, ToolTip = "调整波形与底部指标区高度",
    };

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
    private readonly ScrollViewer narrowScroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private readonly StackPanel narrowStack = new();
    private readonly Grid rightGrid = new();
    private readonly Grid bottomContainer = new();

    private readonly Dictionary<object, Border> rightCardHosts = [];
    private readonly Dictionary<object, Border> bottomCardHosts = [];
    private readonly MultiAnalysisFloatingWindowManager floatingManager;

    private GridLength savedWaveformWidth = new(1, GridUnitType.Star);
    private GridLength savedRightWidth = new(1, GridUnitType.Star);
    private GridLength savedMainHeight = new(1, GridUnitType.Star);
    private GridLength savedBottomHeight = new(260, GridUnitType.Pixel);
    private bool rightWasVisible;
    private bool bottomWasVisible;
    private (int Count, bool Wide, object? Maximized)? lastRightLayoutSignature;

    public MultiAnalysisDockHost()
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
        rightContainer.SizeChanged += (_, _) => UpdateRightLayoutIfNeeded();

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

        floatingManager = new MultiAnalysisFloatingWindowManager(
            () => Window.GetWindow(this),
            () => PanelTemplate,
            slot =>
            {
                if (FloatingWindowClosedCommand?.CanExecute(slot) == true)
                    FloatingWindowClosedCommand.Execute(slot);
            });
        Content = workspace;
        Unloaded += (_, _) => floatingManager.CloseAll();
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

    public void ActivateFloatingPanel(object slot) => floatingManager.Activate(slot);

    private static void OnWaveformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MultiAnalysisDockHost)d;
        if (e.OldValue is FrameworkElement oldElement) host.workspace.Children.Remove(oldElement);
        if (e.NewValue is FrameworkElement newElement)
        {
            Grid.SetColumn(newElement, 0);
            Grid.SetRow(newElement, 0);
            if (!host.workspace.Children.Contains(newElement)) host.workspace.Children.Insert(0, newElement);
        }
    }

    private static void OnRightSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: true, isFloating: false);

    private static void OnBottomSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: false, isFloating: false);

    private static void OnFloatingSlotsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MultiAnalysisDockHost)d).RebindCollection(e.OldValue, e.NewValue, isRight: false, isFloating: true);

    private static void OnPanelTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (MultiAnalysisDockHost)d;
        foreach (var card in host.rightCardHosts.Values.Concat(host.bottomCardHosts.Values))
        {
            if (card.Child is ContentControl presenter) presenter.ContentTemplate = host.PanelTemplate;
        }
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
        else if (isRight) SyncRightSlots();
        else SyncBottomSlots();
    }

    private void OnRightCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncRightSlots();
    private void OnBottomCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncBottomSlots();
    private void OnFloatingCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncFloatingWindows();

    private void SyncRightSlots()
    {
        var slots = Materialize(RightSlots);
        foreach (var slot in slots)
        {
            if (!rightCardHosts.ContainsKey(slot))
            {
                if (slot is INotifyPropertyChanged notify) notify.PropertyChanged += OnSlotPropertyChanged;
                rightCardHosts[slot] = CreateCardContainer(slot);
            }
        }
        foreach (var stale in rightCardHosts.Keys.Except(slots).ToArray())
        {
            if (stale is INotifyPropertyChanged notify) notify.PropertyChanged -= OnSlotPropertyChanged;
            DetachFromParent(rightCardHosts[stale]);
            rightCardHosts.Remove(stale);
        }
        lastRightLayoutSignature = null;
        UpdateWorkspaceVisibility();
        UpdateRightLayoutIfNeeded();
    }

    private void SyncBottomSlots()
    {
        var slots = Materialize(BottomSlots);
        bottomContainer.Children.Clear();
        foreach (var slot in slots)
        {
            if (!bottomCardHosts.TryGetValue(slot, out var container))
            {
                container = CreateCardContainer(slot);
                bottomCardHosts[slot] = container;
            }
            DetachFromParent(container);
            container.BorderThickness = new Thickness(0);
            container.CornerRadius = new CornerRadius(0);
            container.Margin = new Thickness(0);
            bottomContainer.Children.Add(container);
        }
        foreach (var stale in bottomCardHosts.Keys.Except(slots).ToArray())
            bottomCardHosts.Remove(stale);
        UpdateWorkspaceVisibility();
    }

    private void SyncFloatingWindows() => floatingManager.Sync(FloatingSlots);

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is null) return;
        if (e.PropertyName is "IsMaximized")
        {
            lastRightLayoutSignature = null;
            UpdateRightLayoutIfNeeded();
        }
        else if (e.PropertyName is "IsFocused" && rightCardHosts.TryGetValue(sender, out var card))
        {
            ApplyCardChrome(card, sender, Materialize(RightSlots).Count > 1);
            RebuildQuickBar(Materialize(RightSlots));
        }
    }

    private void UpdateWorkspaceVisibility()
    {
        var rightVisible = rightCardHosts.Count > 0;
        var bottomVisible = bottomCardHosts.Count > 0;

        if (rightWasVisible && !rightVisible)
        {
            savedWaveformWidth = waveformColumn.Width;
            savedRightWidth = rightColumn.Width;
        }
        if (bottomWasVisible && !bottomVisible)
        {
            savedMainHeight = mainRow.Height;
            savedBottomHeight = bottomRow.Height;
        }
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
        Grid.SetRow(horizontalSplitter, 1);
        Grid.SetColumn(horizontalSplitter, 0);
        Grid.SetColumnSpan(horizontalSplitter, bottomSpan);
        Grid.SetRow(bottomContainer, 2);
        Grid.SetColumn(bottomContainer, 0);
        Grid.SetColumnSpan(bottomContainer, bottomSpan);
    }

    private void UpdateRightLayoutIfNeeded()
    {
        var slots = Materialize(RightSlots);
        if (slots.Count == 0)
        {
            quickBar.Visibility = Visibility.Collapsed;
            rightSurface.Children.Clear();
            lastRightLayoutSignature = null;
            return;
        }

        var maximized = slots.FirstOrDefault(slot => ReadBoolProperty(slot, "IsMaximized"));
        var visibleSlots = maximized is not null ? [maximized] : slots;
        var availableWidth = rightContainer.ActualWidth > 0 ? rightContainer.ActualWidth : workspace.ActualWidth * 0.5;
        var isWide = availableWidth >= WideTwoColumnMinWidth;
        var signature = (slots.Count, isWide, maximized);
        if (lastRightLayoutSignature == signature) return;
        lastRightLayoutSignature = signature;

        RebuildQuickBar(slots);
        rightSurface.Children.Clear();
        rightGrid.Children.Clear();
        rightGrid.RowDefinitions.Clear();
        rightGrid.ColumnDefinitions.Clear();
        narrowStack.Children.Clear();

        var showMultiChrome = slots.Count > 1;
        if (visibleSlots.Count >= 3 && !isWide)
        {
            foreach (var slot in visibleSlots)
            {
                var card = rightCardHosts[slot];
                DetachFromParent(card);
                ApplyCardChrome(card, slot, showMultiChrome);
                card.MinHeight = NarrowCardMinHeight;
                card.Height = Math.Max(NarrowCardMinHeight, 340);
                narrowStack.Children.Add(card);
            }
            rightSurface.Children.Add(narrowScroller);
            return;
        }

        ArrangeRightGrid(visibleSlots, showMultiChrome);
        rightSurface.Children.Add(rightGrid);
    }

    private void ArrangeRightGrid(IReadOnlyList<object> visibleSlots, bool showMultiChrome)
    {
        var count = visibleSlots.Count;
        if (count == 1)
        {
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PlaceCardInGrid(visibleSlots[0], 0, 0, 1, showMultiChrome);
            return;
        }
        if (count == 2)
        {
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PlaceCardInGrid(visibleSlots[0], 0, 0, 1, showMultiChrome);
            PlaceCardInGrid(visibleSlots[1], 1, 0, 1, showMultiChrome);
            return;
        }

        // Wide 3 or 4 cards: 2x2 grid (card 3 spans full bottom row when count == 3)
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PlaceCardInGrid(visibleSlots[0], 0, 0, 1, showMultiChrome);
        PlaceCardInGrid(visibleSlots[1], 0, 1, 1, showMultiChrome);
        if (count == 3)
        {
            PlaceCardInGrid(visibleSlots[2], 1, 0, 2, showMultiChrome);
        }
        else
        {
            PlaceCardInGrid(visibleSlots[2], 1, 0, 1, showMultiChrome);
            PlaceCardInGrid(visibleSlots[3], 1, 1, 1, showMultiChrome);
        }
    }

    private void PlaceCardInGrid(object slot, int row, int column, int columnSpan, bool showMultiChrome)
    {
        var card = rightCardHosts[slot];
        DetachFromParent(card);
        card.ClearValue(MinHeightProperty);
        card.ClearValue(HeightProperty);
        ApplyCardChrome(card, slot, showMultiChrome);
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, columnSpan);
        rightGrid.Children.Add(card);
    }

    private void RebuildQuickBar(IReadOnlyList<object> slots)
    {
        if (slots.Count <= 1)
        {
            quickBar.Visibility = Visibility.Collapsed;
            quickBarPills.Children.Clear();
            return;
        }
        quickBar.Visibility = Visibility.Visible;
        quickBarPills.Children.Clear();
        quickBarPills.Children.Add(new TextBlock
        {
            Text = $"并行分析 ({slots.Count})：",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
        });
        foreach (var slot in slots)
        {
            var isFocused = ReadBoolProperty(slot, "IsFocused");
            var label = ReadStringProperty(slot, "DisplayName") ?? "算法";
            var button = new Button
            {
                Content = label,
                FontSize = 11.5,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(isFocused ? Color.FromRgb(219, 234, 254) : Color.FromRgb(248, 250, 252)),
                Foreground = new SolidColorBrush(isFocused ? Color.FromRgb(37, 99, 235) : Color.FromRgb(51, 65, 85)),
                BorderBrush = new SolidColorBrush(isFocused ? Color.FromRgb(147, 197, 253) : Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Tag = slot,
            };
            button.Click += (_, _) =>
            {
                if (FocusSlotCommand?.CanExecute(slot) == true) FocusSlotCommand.Execute(slot);
                if (rightCardHosts.TryGetValue(slot, out var host)) host.BringIntoView();
            };
            quickBarPills.Children.Add(button);
        }
    }

    private Border CreateCardContainer(object slot) => new()
    {
        Background = Brushes.White,
        ClipToBounds = true,
        Child = new ContentControl { Content = slot, ContentTemplate = PanelTemplate },
    };

    private static void ApplyCardChrome(Border card, object slot, bool showMultiChrome)
    {
        if (!showMultiChrome)
        {
            card.BorderThickness = new Thickness(0);
            card.CornerRadius = new CornerRadius(0);
            card.Margin = new Thickness(0);
            return;
        }
        var focused = ReadBoolProperty(slot, "IsFocused");
        card.BorderBrush = new SolidColorBrush(focused ? Color.FromRgb(96, 165, 250) : Color.FromRgb(226, 232, 240));
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(8);
        card.Margin = new Thickness(4);
    }

    private static void DetachFromParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
    }

    internal static IReadOnlyList<object> Materialize(IEnumerable? items)
    {
        var list = new List<object>();
        if (items is null) return list;
        foreach (var item in items) if (item is not null) list.Add(item);
        return list;
    }

    private static bool ReadBoolProperty(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName)?.GetValue(target) as bool? == true;

    internal static string? ReadStringProperty(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName)?.GetValue(target) as string;
}
