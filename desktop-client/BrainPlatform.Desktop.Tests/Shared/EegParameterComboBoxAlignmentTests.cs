using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Tests.Shared;

[Collection("WPF application resources")]
public sealed class EegParameterComboBoxAlignmentTests(WpfApplicationFixture fixture)
{
    private static readonly string[] SensitivityLabels =
    [
        "1 µV/mm", "2 µV/mm", "5 µV/mm", "7 µV/mm", "10 µV/mm",
        "15 µV/mm", "20 µV/mm", "50 µV/mm", "70 µV/mm", "100 µV/mm",
    ];

    [Fact]
    public void EegParameterComboBox_popup_aligns_flush_with_outer_field_and_fits_sensitivity_items_without_overflow() =>
        fixture.Run(() =>
        {
            var resources = CreateStyleResources();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(resources);

            var comboBox = new ComboBox
            {
                Style = (Style)resources["EegParameterComboBoxStyle"],
            };
            foreach (var label in SensitivityLabels)
            {
                comboBox.Items.Add(new ComboBoxItem { Content = label, Tag = label });
            }

            comboBox.SelectedIndex = 4; // 10 µV/mm

            var field = new EegParameterField
            {
                Header = "灵敏度",
                MinWidth = 94,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = comboBox,
            };

            var window = CreateHostWindow(field);
            try
            {
                window.Show();
                Drain(window);

                comboBox.IsDropDownOpen = true;
                Drain(window);

                var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
                var dropDownBorder = Assert.IsType<Border>(popup.Child);

                Assert.Same(field, popup.PlacementTarget);
                Assert.Equal(PlacementMode.Bottom, popup.Placement);
                Assert.Equal(-dropDownBorder.Margin.Left, popup.HorizontalOffset, 3);
                Assert.True(dropDownBorder.Margin.Top >= 3);
                Assert.Equal(0, popup.VerticalOffset, 3);
                Assert.Equal(field.ActualWidth, dropDownBorder.MinWidth, 2);
                Assert.True(
                    dropDownBorder.ActualWidth <= field.ActualWidth + 0.5,
                    $"Expected dropdown width ({dropDownBorder.ActualWidth}) <= field width ({field.ActualWidth}).");

                var scrollViewer = FindDescendant<ScrollViewer>(dropDownBorder);
                Assert.NotNull(scrollViewer);
                Assert.Equal(Visibility.Collapsed, scrollViewer.ComputedVerticalScrollBarVisibility);
            }
            finally
            {
                comboBox.IsDropDownOpen = false;
                window.Close();
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(resources);
            }
        });

    [Fact]
    public void EegParameterComboBox_standalone_falls_back_to_combobox_width() =>
        fixture.Run(() =>
        {
            var resources = CreateStyleResources();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(resources);

            var comboBox = new ComboBox
            {
                Style = (Style)resources["EegParameterComboBoxStyle"],
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            comboBox.Items.Add("50 Hz");
            comboBox.Items.Add("60 Hz");
            comboBox.SelectedIndex = 0;

            var window = CreateHostWindow(comboBox);
            try
            {
                window.Show();
                Drain(window);

                comboBox.IsDropDownOpen = true;
                Drain(window);

                var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
                var dropDownBorder = Assert.IsType<Border>(popup.Child);

                Assert.Null(popup.PlacementTarget);
                Assert.Equal(comboBox.ActualWidth, dropDownBorder.MinWidth, 2);
            }
            finally
            {
                comboBox.IsDropDownOpen = false;
                window.Close();
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(resources);
            }
        });

    [Theory]
    [InlineData("AnalysisHeaderComboBoxStyle")]
    [InlineData("AppFilterComboBoxStyle")]
    [InlineData("AppRoundedFilterComboBoxStyle")]
    public void Shared_combobox_popups_cancel_left_shadow_margin_for_exact_alignment(string styleKey) =>
        fixture.Run(() =>
        {
            var resources = CreateStyleResources();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(resources);

            var comboBox = new ComboBox
            {
                Style = (Style)resources[styleKey],
                Width = 140,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            comboBox.Items.Add("Alpha");
            comboBox.Items.Add("Beta");
            comboBox.SelectedIndex = 0;

            var window = CreateHostWindow(comboBox);
            try
            {
                window.Show();
                Drain(window);

                comboBox.IsDropDownOpen = true;
                Drain(window);

                var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
                var dropDownBorder = Assert.IsType<Border>(popup.Child);

                Assert.Equal(PlacementMode.Bottom, popup.Placement);
                Assert.Equal(-dropDownBorder.Margin.Left, popup.HorizontalOffset, 3);
                Assert.Equal(comboBox.ActualWidth, dropDownBorder.MinWidth, 2);
            }
            finally
            {
                comboBox.IsDropDownOpen = false;
                window.Close();
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(resources);
            }
        });

    private static ResourceDictionary CreateStyleResources()
    {
        var dictionary = new ResourceDictionary();
        foreach (var path in new[]
        {
            "/BrainPlatform.Desktop;component/Shared/Styles/BrandColors.xaml",
            "/BrainPlatform.Desktop;component/Shared/Styles/ControlStyles.xaml",
            "/BrainPlatform.Desktop;component/Shared/Styles/AnalysisStyles.xaml",
            "/BrainPlatform.Desktop;component/Shared/Styles/ScrollBarStyles.xaml",
            "/BrainPlatform.Desktop;component/Shared/Styles/InstrumentWorkspaceStyles.xaml",
        })
        {
            dictionary.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(path, UriKind.RelativeOrAbsolute),
            });
        }

        return dictionary;
    }

    private static Window CreateHostWindow(UIElement content) => new()
    {
        Width = 400,
        Height = 300,
        Left = -20000,
        Top = -20000,
        ShowActivated = false,
        ShowInTaskbar = false,
        Content = new Grid
        {
            Margin = new Thickness(16),
            Children = { content },
        },
    };

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

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
