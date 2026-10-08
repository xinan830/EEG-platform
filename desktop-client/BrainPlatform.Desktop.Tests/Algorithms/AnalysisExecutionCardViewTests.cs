using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AnalysisExecutionCardViewTests
{
    [Theory]
    [InlineData(280, false)]
    [InlineData(280, true)]
    [InlineData(420, false)]
    [InlineData(420, true)]
    public void Actions_AlignAndRetainCommandAndCancellationBindings(double width, bool running)
    {
        OnSta(() =>
        {
            var context = new ExecutionContext(running);
            var card = new AnalysisExecutionCard { DataContext = context };
            card.Measure(new Size(width, 800));
            card.Arrange(new Rect(0, 0, width, card.DesiredSize.Height));
            card.UpdateLayout();

            var retry = Assert.IsType<Button>(card.FindName("RetryAnalysisButton"));
            var cancel = Assert.IsType<Button>(card.FindName("CancelAnalysisButton"));
            var actions = Assert.IsType<Grid>(card.FindName("ExecutionActions"));
            Assert.Same(context.RunCommand, retry.Command);
            Assert.Same(context.CancelRunCommand, cancel.Command);
            Assert.Equal(!running, retry.IsEnabled);
            Assert.Equal(running, cancel.IsEnabled);
            Assert.Equal(34, retry.ActualHeight);
            Assert.Equal(retry.ActualHeight, cancel.ActualHeight);
            Assert.Equal(retry.ActualWidth, cancel.ActualWidth, precision: 5);
            var retryOrigin = retry.TranslatePoint(new Point(), card);
            var cancelOrigin = cancel.TranslatePoint(new Point(), card);
            Assert.Equal(retryOrigin.Y, cancelOrigin.Y, precision: 5);
            Assert.Equal(8, cancelOrigin.X - retryOrigin.X - retry.ActualWidth, precision: 5);
            Assert.True(cancelOrigin.X + cancel.ActualWidth <= card.ActualWidth);
            Assert.True(actions.ActualWidth <= card.ActualWidth);

            AssertButtonContentFits(retry);
            AssertButtonContentFits(cancel);
            var disabled = running ? retry : cancel;
            var border = Assert.IsType<Border>(disabled.Template.FindName("ButtonBorder", disabled));
            Assert.Equal(new CornerRadius(6), border.CornerRadius);
            Assert.Equal(Color.FromRgb(0x94, 0xA3, 0xB8), ((SolidColorBrush)disabled.Foreground).Color);
            Assert.Equal(Color.FromRgb(0xF8, 0xFA, 0xFC), ((SolidColorBrush)border.Background).Color);
            foreach (var label in ((StackPanel)disabled.Content).Children.OfType<TextBlock>())
                Assert.Equal(((SolidColorBrush)disabled.Foreground).Color, ((SolidColorBrush)label.Foreground).Color);

            SavePreviewWhenRequested(card, width, running);
        });
    }

    private static void AssertButtonContentFits(Button button)
    {
        var content = Assert.IsType<StackPanel>(button.Content);
        Assert.True(content.DesiredSize.Width + button.Padding.Left + button.Padding.Right
            + button.BorderThickness.Left + button.BorderThickness.Right <= button.ActualWidth);
        Assert.True(content.DesiredSize.Height <= button.ActualHeight);
    }

    private static void SavePreviewWhenRequested(FrameworkElement card, double width, bool running)
    {
        var directory = Environment.GetEnvironmentVariable("BRAIN_EXECUTION_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),
            (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"execution-{width}-{(running ? "running" : "idle")}.png"));
        encoder.Save(stream);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class ExecutionContext(bool running)
    {
        public ICommand RunCommand { get; } = new TestCommand(!running);
        public ICommand CancelRunCommand { get; } = new TestCommand(running);
        public bool CanCancelRun => running;
        public bool IsDynamicMode => false;
        public double RunProgressPercent => running ? 37.5 : 0;
        public string RunProgressText => running ? "3/8 (37.5%)" : "未运行";
    }

    private sealed class TestCommand(bool enabled) : ICommand
    {
        public bool CanExecute(object? parameter) => enabled;
        public void Execute(object? parameter) { }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
