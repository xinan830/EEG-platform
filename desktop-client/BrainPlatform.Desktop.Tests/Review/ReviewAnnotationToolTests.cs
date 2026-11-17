using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml.Linq;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public void ReviewToolbarHostsAnnotationToolWithoutInlineEventControls()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordingReviewView.xaml"));
        Assert.Single(document.Descendants(), element => element.Name.LocalName == "ReviewAnnotationTool");
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute =>
            attribute.Value.Contains("MarkSelectedEventCommand", StringComparison.Ordinal) ||
            attribute.Value.Contains("IsEventEditorOpen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnnotationWindowOpensWithoutMarkingAndKeepsCommandsAndPermissions()
    {
        await using var model = AnnotationModel();
        WpfTestApplication.Run(() =>
        {
            var tool = new ReviewAnnotationTool { DataContext = model };
            var window = ShowAnnotationTool(tool);
            try
            {
                var button = Find<Button>(tool, "AnnotationButton");
                Assert.True(button.IsEnabled);
                Assert.Equal("事件标记", button.Content);
                Assert.Null(button.Command);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpAnnotationUi();

                var panel = Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>());
                Assert.True(tool.IsOpen);
                Assert.True(panel.IsVisible);
                Assert.Equal("事件标记", panel.Title);
                var sectionTitles = AnnotationVisualChildren(panel).OfType<TextBlock>().Select(label => label.Text).ToArray();
                Assert.True(Array.IndexOf(sectionTitles, "已标记事件") < Array.IndexOf(sectionTitles, "添加新事件"));
                Assert.DoesNotContain("当前回溯位置", sectionTitles);
                Assert.Equal(Visibility.Visible, Find<Border>(panel, "HistoryEmptyState").Visibility);
                Assert.Equal(Visibility.Collapsed, Find<StackPanel>(panel, "HistoryControls").Visibility);
                Assert.True(window.IsEnabled);
                Assert.Same(window, panel.Owner);
                Assert.Same(model, panel.DataContext);
                Assert.Empty(model.RecordingEvents);
                Assert.False(Find<Button>(panel, "MarkButton").IsEnabled);
                Assert.False(Find<Button>(panel, "WaveformAddButton").IsEnabled);
                Assert.Equal("AddEventTimeText", Find<TextBox>(panel, "AddTimeBox").GetBindingExpression(TextBox.TextProperty)!.ParentBinding.Path.Path);
                Assert.False(Find<Button>(panel, "JumpButton").IsEnabled);
                Assert.False(Find<Button>(panel, "EditButton").IsEnabled);
                Assert.False(Find<Button>(panel, "DeleteButton").IsEnabled);

                var definition = new EventDefinition("definition", "EO", "睁眼", "", "#2563EB",
                    true, null, ShortcutScope.Review, EventDefinitionSource.User, 1,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                model.EnabledEventDefinitions.Add(definition);
                var selector = Find<ComboBox>(panel, "DefinitionSelector");
                selector.IsDropDownOpen = true;
                PumpAnnotationUi();
                selector.SelectedItem = definition;
                selector.IsDropDownOpen = false;
                PumpAnnotationUi();
                Assert.True(tool.IsOpen);
                Assert.Equal(definition.Id, model.SelectedEventDefinitionId);
                Assert.Contains(AnnotationVisualChildren(selector).OfType<TextBlock>(), label => label.Text == "睁眼");
                Assert.True(Find<Button>(panel, "MarkButton").IsEnabled);
                Assert.Equal(Visibility.Collapsed, Find<TextBlock>(panel, "DefinitionEmptyHint").Visibility);
                Assert.Same(model.MarkSelectedEventCommand, Find<Button>(panel, "MarkButton").Command);

                var manual = new RecordingEvent("manual", "recording", definition.Id,
                    new EventDefinitionSnapshot("EO", "睁眼", "#2563EB", 1),
                    EventSource.ManualButton, "review", null, 500, 0,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                model.RecordingEventOptions.Add(new RecordingEventOption(manual, "00:01 睁眼", "睁眼"));
                var history = Find<ComboBox>(panel, "HistorySelector");
                history.IsDropDownOpen = true;
                PumpAnnotationUi();
                history.SelectedIndex = 0;
                history.IsDropDownOpen = false;
                PumpAnnotationUi();
                Assert.True(tool.IsOpen);
                Assert.Same(manual, model.SelectedRecordingEvent);
                Assert.Equal(Visibility.Collapsed, Find<Border>(panel, "HistoryEmptyState").Visibility);
                Assert.Equal(Visibility.Visible, Find<StackPanel>(panel, "HistoryControls").Visibility);
                Assert.True(Find<Button>(panel, "EditButton").IsEnabled);
                Assert.True(Find<Button>(panel, "DeleteButton").IsEnabled);
                var editButton = Find<Button>(panel, "EditButton");
                model.OpenEventEditorCommand.Execute(null);
                PumpAnnotationUi();
                Assert.Equal(Visibility.Visible, Find<StackPanel>(panel, "EventEditor").Visibility);
                Assert.Equal(1, model.EditEventStartSeconds);
                Assert.Equal("00:00:01.000", Find<TextBox>(panel, "EditTimeBox").Text);
                Assert.Same(model.BeginWaveformMoveCommand, Find<Button>(panel, "WaveformMoveButton").Command);
                Assert.Equal(Color.FromRgb(219, 234, 254), Assert.IsType<SolidColorBrush>(editButton.Background).Color);
                model.BeginEventPlacement(true);
                model.OpenEventEditorCommand.Execute(null);
                PumpAnnotationUi();
                Assert.Equal(Visibility.Collapsed, Find<StackPanel>(panel, "EventEditor").Visibility);
                Assert.False(model.IsEventEditorOpen);
                Assert.False(model.IsEventPlacementActive);
                Assert.NotEqual(Color.FromRgb(219, 234, 254), Assert.IsType<SolidColorBrush>(editButton.Background).Color);
                model.OpenEventEditorCommand.Execute(null);
                PumpAnnotationUi();
                Assert.DoesNotContain(AnnotationVisualChildren(panel).OfType<TextBlock>(),
                    label => label.Text is "起始时间" or "持续时间");

                model.SelectedRecordingEvent = manual with { Source = EventSource.ImportedAnnotation };
                PumpAnnotationUi();
                Assert.False(Find<Button>(panel, "EditButton").IsEnabled);
                Assert.False(Find<Button>(panel, "DeleteButton").IsEnabled);
                Assert.False(model.OpenEventEditorCommand.CanExecute(null));
                Assert.Equal(Visibility.Collapsed, Find<StackPanel>(panel, "EventEditor").Visibility);

                // Re-activation keeps the same window and editing state until explicitly closed.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpAnnotationUi();
                Assert.True(tool.IsOpen);
                Assert.Same(panel, Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>()));
                Find<Button>(panel, "CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(tool.IsOpen);
                Assert.False(model.IsEventEditorOpen);
                Assert.Empty(model.RecordingEvents);
            }
            finally { tool.IsOpen = false; window.Close(); }
        });
    }

    [Fact]
    public async Task AnnotationWindowPersistsWhileWorkingAndClosesOnlyExplicitlyOrWithSession()
    {
        await using var model = AnnotationModel();
        WpfTestApplication.Run(() =>
        {
            var tool = new ReviewAnnotationTool { DataContext = model };
            var window = ShowAnnotationTool(tool);
            try
            {
                tool.IsOpen = true;
                PumpAnnotationUi();
                var panel = Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>());
                Assert.Equal(WindowStyle.None, panel.WindowStyle);
                var chrome = WindowChrome.GetWindowChrome(panel);
                Assert.NotNull(chrome);
                Assert.Equal(46, chrome.CaptionHeight);
                Assert.True(chrome.ResizeBorderThickness.Left > 0);
                Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(Find<Button>(panel, "CloseButton")));
                Assert.Equal(ResizeMode.CanResize, panel.ResizeMode);
                Assert.False(panel.ShowInTaskbar);
                Assert.False(panel.Topmost);
                Assert.True(window.IsEnabled);
                var ownerOrigin = window.PointToScreen(new Point());
                ownerOrigin = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformFromDevice.Transform(ownerOrigin);
                Assert.InRange(panel.Left - ownerOrigin.X, window.ActualWidth - panel.Width - 20, window.ActualWidth - panel.Width);

                panel.Left += 35;
                panel.Top += 25;
                panel.Width += 20;
                var left = panel.Left;
                var top = panel.Top;
                model.IsEventEditorOpen = true;
                model.EditEventNote = "未保存备注";
                var type = new EventDefinition("escape-type", "EO", "睁眼", "", "#2563EB", true, null,
                    ShortcutScope.Review, EventDefinitionSource.User, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                model.EnabledEventDefinitions.Add(type);
                model.SelectedEventDefinitionId = type.Id;
                model.BeginEventPlacement(false);
                Assert.True(model.IsEventPlacementActive);
                foreach (var surface in new Window[] { panel, window })
                {
                    surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(surface),
                        Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    surface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount,
                        MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                }
                var workCount = 0;
                Assert.False(model.IsEventPlacementActive);
                var work = new Button { Command = new RelayCommand(() => workCount++) };
                var layout = Assert.IsType<StackPanel>(window.Content);
                layout.Children.Add(work);
                work.Command.Execute(null);
                window.Activate();
                Find<Button>(tool, "AnnotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpAnnotationUi();
                Assert.Equal(1, workCount);
                Assert.True(tool.IsOpen);
                Assert.True(panel.IsVisible);
                Assert.True(model.IsEventEditorOpen);
                Assert.Equal("未保存备注", model.EditEventNote);
                Assert.Equal(left, panel.Left);
                Assert.Equal(top, panel.Top);
                Assert.Same(panel, Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>()));

                panel.Close();
                PumpAnnotationUi();
                Assert.False(tool.IsOpen);
                Assert.False(model.IsEventEditorOpen);
                Assert.Empty(window.OwnedWindows.OfType<ReviewAnnotationWindow>());
                Find<Button>(tool, "AnnotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpAnnotationUi();
                var reopened = Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>());
                Assert.NotSame(panel, reopened);
                Assert.Same(model, reopened.DataContext);
                window.Content = null;
                PumpAnnotationUi();
                Assert.False(tool.IsOpen);
                Assert.False(reopened.IsVisible);
            }
            finally { tool.IsOpen = false; window.Close(); }
        });
    }

    [Fact]
    public async Task AnnotationWindowIsDisposedWhenRecordingContextChangesOrOwnerCloses()
    {
        await using var model = AnnotationModel();
        WpfTestApplication.Run(() =>
        {
            var tool = new ReviewAnnotationTool { DataContext = model };
            var owner = ShowAnnotationTool(tool);
            try
            {
                tool.IsOpen = true;
                PumpAnnotationUi();
                var panel = Assert.Single(owner.OwnedWindows.OfType<ReviewAnnotationWindow>());
                tool.DataContext = null;
                PumpAnnotationUi();
                Assert.False(tool.IsOpen);
                Assert.False(panel.IsVisible);
                Assert.Null(panel.DataContext);
                tool.DataContext = model;
                tool.IsOpen = true;
                PumpAnnotationUi();
                panel = Assert.Single(owner.OwnedWindows.OfType<ReviewAnnotationWindow>());
                owner.Close();
                PumpAnnotationUi();
                Assert.False(tool.IsOpen);
                Assert.False(panel.IsVisible);
            }
            finally { tool.IsOpen = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false, 1d, false)]
    [InlineData(true, 1d, false)]
    [InlineData(false, 1.5d, false)]
    [InlineData(true, 1.5d, false)]
    [InlineData(true, 1d, true)]
    [InlineData(true, 1.5d, true)]
    public async Task AnnotationWindowRendersCompiledPanel(bool hasEvents, double scale, bool editing)
    {
        await using var model = AnnotationModel();
        WpfTestApplication.Run(() =>
        {
            if (hasEvents)
            {
                model.EnabledEventDefinitions.Add(new EventDefinition("eo", "EO", "睁眼", "", "#2563EB",
                    true, null, ShortcutScope.Review, EventDefinitionSource.User, 1,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
                model.SelectedEventDefinitionId = "eo";
                var item = new RecordingEvent("preview", "recording", "eo",
                    new EventDefinitionSnapshot("EO", "睁眼", "#2563EB", 1),
                    EventSource.ManualButton, "review", null, 500, 0,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                model.RecordingEventOptions.Add(new RecordingEventOption(item, "00:01 睁眼", "睁眼"));
                model.SelectedRecordingEvent = item;
            }
            var tool = new ReviewAnnotationTool { DataContext = model };
            var window = ShowAnnotationTool(tool);
            try
            {
                tool.IsOpen = true;
                PumpAnnotationUi();
                var panel = Assert.Single(window.OwnedWindows.OfType<ReviewAnnotationWindow>());
                if (editing)
                {
                    panel.Height = 850;
                    model.IsEventEditorOpen = true;
                    model.EditEventNote = "睁眼事件，可输入时间或在波形上改位";
                    model.BeginEventPlacement(true);
                    PumpAnnotationUi();
                }
                var surface = Find<Border>(panel, "PanelSurface");
                Assert.InRange(surface.ActualWidth, 378, 390);
                Assert.InRange(surface.ActualHeight, 250, 850);
                Assert.Equal(hasEvents ? Visibility.Collapsed : Visibility.Visible, Find<Border>(panel, "HistoryEmptyState").Visibility);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale),
                    (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var path = Environment.GetEnvironmentVariable("ANNOTATION_PREVIEW_PATH");
                if (path is not null)
                {
                    path = Path.Combine(Path.GetDirectoryName(path)!,
                        $"{Path.GetFileNameWithoutExtension(path)}-{(editing ? "editing" : hasEvents ? "events" : "empty")}-{scale * 100:0}.png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
                }
            }
            finally { tool.IsOpen = false; window.Close(); }
        });
    }

    private static RecordingReviewViewModel AnnotationModel()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        return new RecordingReviewViewModel(new FakeReader(Manifest(configuration)), catalog);
    }

    private static Window ShowAnnotationTool(ReviewAnnotationTool tool)
    {
        var window = new Window { Content = new StackPanel { Children = { tool } }, Width = 700, Height = 720,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000 };
        tool.HorizontalAlignment = HorizontalAlignment.Left;
        tool.VerticalAlignment = VerticalAlignment.Top;
        window.Show();
        PumpAnnotationUi();
        return window;
    }

    private static T Find<T>(FrameworkElement element, string name) where T : class =>
        Assert.IsAssignableFrom<T>(element.FindName(name));

    private static IEnumerable<DependencyObject> AnnotationVisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in AnnotationVisualChildren(child)) yield return descendant;
        }
    }

    private static void PumpAnnotationUi() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
