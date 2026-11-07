using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Tests.Shared;

[Collection("WPF application resources")]
public sealed class SlimScrollBarStyleTests(WpfApplicationFixture fixture)
{
    [Theory]
    [InlineData("panel")]
    [InlineData("table")]
    [InlineData("list")]
    [InlineData("text")]
    public void Shared_style_reduces_both_tracks_and_preserves_paging_and_dragging(string kind) => fixture.Run(() =>
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("/BrainPlatform.Desktop;component/Shared/Styles/ScrollBarStyles.xaml", UriKind.RelativeOrAbsolute),
        };
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(resources);
        var content = Content(kind);
        var window = new Window { Width = 360, Height = 280, Content = content, ShowInTaskbar = false };
        try
        {
            window.Show();
            Drain(window);
            var viewer = Descendants<ScrollViewer>(content).Prepend(content as ScrollViewer)
                .OfType<ScrollViewer>().First(item => item.ScrollableHeight > 0 && item.ScrollableWidth > 0);
            var bars = Descendants<ScrollBar>(viewer).Where(bar => bar.IsVisible).ToArray();
            Assert.Equal(2, bars.Length);
            foreach (var bar in bars)
            {
                Assert.Same(resources[typeof(ScrollBar)], bar.Style);
                Verify(bar);
            }

            // Existing list pages still resolve their explicit shared resource key.
            var vertical = bars.Single(bar => bar.Orientation == Orientation.Vertical);
            vertical.Style = (Style)resources["SlimTableScrollBarStyle"];
            Drain(window);
            Verify(vertical);

            void Verify(ScrollBar bar)
            {
                var horizontal = bar.Orientation == Orientation.Horizontal;
                Assert.Equal(6, horizontal ? bar.ActualHeight : bar.ActualWidth);
                Assert.True((horizontal ? bar.ActualWidth : bar.ActualHeight) > 100);
                var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
                Assert.Equal(!horizontal, track.IsDirectionReversed);
                var thumb = track.Thumb;
                Assert.Equal(4, horizontal ? thumb.ActualHeight : thumb.ActualWidth);
                var border = Assert.IsType<Border>(thumb.Template.FindName("ThumbBorder", thumb));
                Assert.Equal(new CornerRadius(2), border.CornerRadius);
                Assert.Contains(thumb.Template.Triggers.OfType<Trigger>(), trigger => trigger.Property == Thumb.IsDraggingProperty);

                viewer.ScrollToHorizontalOffset(0);
                viewer.ScrollToVerticalOffset(0);
                Drain(window);
                var increase = track.IncreaseRepeatButton;
                var command = Assert.IsType<RoutedCommand>(increase.Command);
                Assert.Same(horizontal ? ScrollBar.PageRightCommand : ScrollBar.PageDownCommand, command);
                Assert.True(command.CanExecute(null, bar));
                command.Execute(null, bar);
                Drain(window);
                var offset = horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
                Assert.True(offset > 0);
                Assert.IsType<RoutedCommand>(track.DecreaseRepeatButton.Command).Execute(null, bar);
                Drain(window);
                Assert.True((horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset) < offset);

                viewer.ScrollToHorizontalOffset(0);
                viewer.ScrollToVerticalOffset(0);
                Drain(window);
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                thumb.RaiseEvent(new DragDeltaEventArgs(horizontal ? 20 : 0, horizontal ? 0 : 20)
                    { RoutedEvent = Thumb.DragDeltaEvent });
                thumb.RaiseEvent(new DragCompletedEventArgs(horizontal ? 20 : 0, horizontal ? 0 : 20, false)
                    { RoutedEvent = Thumb.DragCompletedEvent });
                Drain(window);
                Assert.True((horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset) > 0);
            }
        }
        finally
        {
            window.Close();
            System.Windows.Application.Current.Resources.MergedDictionaries.Remove(resources);
        }
    });

    private static FrameworkElement Content(string kind)
    {
        switch (kind)
        {
            case "panel":
                return new ScrollViewer
                {
                    Content = new Border { Width = 900, Height = 800 },
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
            case "table":
                var table = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false };
                table.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Name"), Width = 450 });
                table.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Name"), Width = 450 });
                table.ItemsSource = Enumerable.Range(0, 50).Select(index => new { Name = $"Channel {index}" }).ToArray();
                return table;
            case "list":
                var list = new ListBox();
                ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
                foreach (var index in Enumerable.Range(0, 50))
                    list.Items.Add(new TextBlock { Text = $"Channel {index}", Width = 900, Height = 24 });
                return list;
            default:
                return new TextBox
                {
                    Text = string.Join(Environment.NewLine, Enumerable.Repeat(new string('x', 200), 50)),
                    AcceptsReturn = true,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
        }
    }

    private static void Drain(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
