using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class SharedAnalysisCardStyleTests
{
    [Theory]
    [InlineData(0, "执行分析")]
    [InlineData(1, "质量状态")]
    [InlineData(2, "结果溯源")]
    [InlineData(3, "动态结果（按时间）")]
    public void Standalone_cards_resolve_and_honor_shared_frame_and_title_resources(int kind, string title)
    {
        OnSta(() =>
        {
            UserControl card = kind switch
            {
                0 => new AnalysisExecutionCard(),
                1 => new QualityStatusCard(),
                2 => new ResultProvenanceCard(),
                _ => new DynamicResultCard(),
            };
            var frame = Assert.IsType<Border>(card.Content);
            var context = new object();
            card.DataContext = context;
            card.Measure(new Size(900, 1000));
            card.Arrange(new Rect(0, 0, 900, card.DesiredSize.Height));
            card.UpdateLayout();

            Assert.Same(card.FindResource("AnalysisCardBorderStyle"), frame.Style);
            Assert.Same(context, frame.DataContext);
            var heading = Descendants(card).OfType<TextBlock>().Single(block => block.Text == title);
            Assert.Same(card.FindResource("AnalysisCardTitleStyle"), heading.Style);
            Assert.Equal(15, heading.FontSize);
            Assert.Equal(new Thickness(20, kind == 2 ? 16 : 18, 20, kind == 2 ? 16 : 18), frame.Padding);

            var borderBrush = new SolidColorBrush(Colors.DarkCyan);
            var headingBrush = new SolidColorBrush(Colors.DarkGreen);
            card.Resources["AnalysisDividerBrush"] = borderBrush;
            card.Resources["AnalysisHeadingBrush"] = headingBrush;
            card.UpdateLayout();
            Assert.Same(borderBrush, frame.BorderBrush);
            Assert.Same(headingBrush, heading.Foreground);
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
