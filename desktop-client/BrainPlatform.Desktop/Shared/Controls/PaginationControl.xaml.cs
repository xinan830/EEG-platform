using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BrainPlatform.Desktop.Shared.Controls;

public partial class PaginationControl : UserControl
{
    public static readonly DependencyProperty CurrentPageProperty = DependencyProperty.Register(
        nameof(CurrentPage),
        typeof(int),
        typeof(PaginationControl),
        new PropertyMetadata(1, OnPaginationPropertyChanged));

    public static readonly DependencyProperty TotalPagesProperty = DependencyProperty.Register(
        nameof(TotalPages),
        typeof(int),
        typeof(PaginationControl),
        new PropertyMetadata(1, OnPaginationPropertyChanged));

    public PaginationControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RebuildButtons();
    }

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int TotalPages
    {
        get => (int)GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public event EventHandler<PageRequestedEventArgs>? PageRequested;

    private static void OnPaginationPropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is PaginationControl control && control.IsLoaded)
        {
            control.RebuildButtons();
        }
    }

    private void RebuildButtons()
    {
        PageButtonHost.Children.Clear();
        var totalPages = Math.Max(1, TotalPages);
        var currentPage = Math.Clamp(CurrentPage, 1, totalPages);
        var visiblePages = BuildVisiblePages(currentPage, totalPages);

        // The control is hosted in a right-aligned footer. Lock its measured
        // width so a template/state change cannot move the whole paginator.
        const double navigationButtonWidth = 40;
        const double pageSlotWidth = 48; // 40px page + 8px leading gap.
        Width = navigationButtonWidth + (visiblePages.Count * pageSlotWidth) + pageSlotWidth;
        PageButtonHost.Width = Width;

        PageButtonHost.Children.Add(CreateNavigationButton(false, currentPage > 1, currentPage - 1));
        foreach (var page in visiblePages)
        {
            if (page is null)
            {
                PageButtonHost.Children.Add(new TextBlock
                {
                    Text = "...",
                    // An ellipsis must reserve exactly one page-button slot.
                    // Otherwise a right-aligned paginator shifts whenever a
                    // numbered page is replaced by an ellipsis.
                    Width = 40,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                continue;
            }

            if (page.Value == currentPage)
            {
                PageButtonHost.Children.Add(new Border
                {
                    Margin = new Thickness(8, 0, 0, 0),
                    Style = (Style)FindResource("PaginationCurrentPageStyle"),
                    Child = new TextBlock
                    {
                        Text = page.Value.ToString(),
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
                continue;
            }

            var button = new Button
            {
                Content = page.Value,
                Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)FindResource("PaginationPageButtonStyle"),
                Tag = page.Value,
            };
            button.Click += OnPageButtonClick;
            PageButtonHost.Children.Add(button);
        }

        var nextButton = CreateNavigationButton(true, currentPage < totalPages, currentPage + 1);
        nextButton.Margin = new Thickness(8, 0, 0, 0);
        PageButtonHost.Children.Add(nextButton);
    }

    private Button CreateNavigationButton(bool pointsRight, bool isEnabled, int requestedPage)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(pointsRight ? "M 5,2 L 12,9 L 5,16" : "M 12,2 L 5,9 L 12,16"),
            Width = 10,
            Height = 16,
            Stretch = Stretch.Fill,
            Stroke = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var button = new Button
        {
            Content = icon,
            IsEnabled = isEnabled,
            Style = (Style)FindResource("PaginationNavigationButtonStyle"),
            Tag = requestedPage,
        };
        button.Click += OnPageButtonClick;
        return button;
    }

    private void OnPageButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int page })
        {
            PageRequested?.Invoke(this, new PageRequestedEventArgs(page));
        }
    }

    private static IReadOnlyList<int?> BuildVisiblePages(int currentPage, int totalPages)
    {
        if (totalPages <= 7)
        {
            return Enumerable.Range(1, totalPages).Select(page => (int?)page).ToArray();
        }

        if (currentPage <= 4)
        {
            return [1, 2, 3, 4, 5, null, totalPages];
        }

        if (currentPage >= totalPages - 3)
        {
            return [1, null, totalPages - 4, totalPages - 3, totalPages - 2, totalPages - 1, totalPages];
        }

        return [1, null, currentPage - 1, currentPage, currentPage + 1, null, totalPages];
    }
}

public sealed class PageRequestedEventArgs(int page) : EventArgs
{
    public int Page { get; } = page;
}
