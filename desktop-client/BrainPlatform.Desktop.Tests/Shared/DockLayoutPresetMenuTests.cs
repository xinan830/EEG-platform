using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

namespace BrainPlatform.Desktop.Tests.Shared;

[Collection("WPF application resources")]
public sealed class DockLayoutPresetMenuTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void Layout_preset_menu_uses_dropdown_style_and_anchors_to_button() =>
        fixture.Run(() =>
        {
            var application = System.Windows.Application.Current;
            var styles = new ResourceDictionary
            {
                Source = new Uri("/BrainPlatform.Desktop;component/Shared/Styles/AnalysisStyles.xaml", UriKind.RelativeOrAbsolute),
            };
            application.Resources.MergedDictionaries.Add(styles);

            var button = new Button
            {
                Content = "排版布局 ▾",
                Width = 160,
                Height = 36,
                Margin = new Thickness(32, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var menu = new ContextMenu { Style = (Style)styles["AppDropdownContextMenuStyle"] };
            menu.Items.Add(new MenuItem
            {
                Header = "经典布局（左波形 · 右自适应网格）",
                Style = (Style)styles["AppDropdownMenuItemStyle"],
            });
            button.ContextMenu = menu;

            var window = new Window
            {
                Width = 800,
                Height = 300,
                Left = 20,
                Top = 20,
                Opacity = 0,
                ShowActivated = false,
                ShowInTaskbar = false,
                Content = new Grid { Children = { button } },
            };

            try
            {
                window.Show();
                Drain(window);

                DockLayoutPresetMenu.Open(button);
                Drain(window);

                Assert.True(menu.IsOpen);
                Assert.Same(button, menu.PlacementTarget);
                Assert.Equal(PlacementMode.Bottom, menu.Placement);
                Assert.Equal(0, menu.HorizontalOffset, 2);
                Assert.Equal(0, menu.VerticalOffset, 2);

                var border = Assert.IsType<Border>(menu.Template.FindName("MenuBorder", menu));
                Assert.Equal(new CornerRadius(6), border.CornerRadius);
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(border.Background).Color);
                Assert.Equal(Color.FromRgb(226, 232, 240), Assert.IsType<SolidColorBrush>(border.BorderBrush).Color);
                Assert.Equal(13, menu.FontSize);
                Assert.Equal(
                    button.PointToScreen(new Point(0, button.ActualHeight)).X,
                    menu.PointToScreen(new Point(0, 0)).X,
                    1);
                Assert.Equal(
                    button.PointToScreen(new Point(0, button.ActualHeight)).Y,
                    menu.PointToScreen(new Point(0, 0)).Y,
                    1);
            }
            finally
            {
                menu.IsOpen = false;
                window.Close();
                application.Resources.MergedDictionaries.Remove(styles);
            }
        });

    private static void Drain(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
}
