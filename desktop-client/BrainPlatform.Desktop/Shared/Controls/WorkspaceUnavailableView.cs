using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Explicit empty state for a navigation destination that has not been built.
/// It prevents navigation clicks from silently keeping the previous workspace.
/// </summary>
public sealed class WorkspaceUnavailableView : UserControl
{
    public WorkspaceUnavailableView(string title)
    {
        Content = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(243, 245, 248)),
            Children =
            {
                new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(32),
                    Margin = new Thickness(28),
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 22,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                                HorizontalAlignment = HorizontalAlignment.Center,
                            },
                            new TextBlock
                            {
                                Text = "该工作区尚未接入。当前不会创建或修改任何采集、项目或算法数据。",
                                Margin = new Thickness(0, 10, 0, 0),
                                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                                HorizontalAlignment = HorizontalAlignment.Center,
                            },
                        },
                    },
                },
            },
        };
    }
}
