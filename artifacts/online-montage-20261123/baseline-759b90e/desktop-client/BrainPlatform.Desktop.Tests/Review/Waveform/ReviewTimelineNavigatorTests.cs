using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewTimelineNavigatorTests
{
    [Theory]
    [InlineData(100, 10, 640)]
    [InlineData(3, 10, 640)]
    [InlineData(10000, 5, 220)]
    public void Thumb_travel_covers_full_recording_and_round_trips_position(double duration, double visible, double width)
    {
        OnSta(() =>
        {
            var navigator = new ReviewTimelineNavigator { DurationSeconds = duration, VisibleDurationSeconds = visible };
            navigator.Measure(new Size(width, 30));
            navigator.Arrange(new Rect(0, 0, width, 30));
            var type = typeof(ReviewTimelineNavigator);
            var position = type.GetProperty("PositionSeconds");
            Assert.NotNull(position);
            var getThumb = type.GetMethod("GetThumbRect", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var getPosition = type.GetMethod("GetPositionForThumbX", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var seconds in new[] { 0, duration * 0.02, duration / 2, duration * 0.98, duration })
            {
                position.SetValue(navigator, seconds);
                var thumb = (Rect)getThumb.Invoke(navigator, null)!;
                Assert.InRange(thumb.Left, 0, width);
                Assert.InRange(thumb.Right, 0, width);
                Assert.Equal(seconds, (double)getPosition.Invoke(navigator, [thumb.Left])!, 6);
                type.GetMethod("BeginDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(navigator, [new Point(thumb.Left + thumb.Width / 2, thumb.Top), thumb]);
                Assert.Equal(seconds, (double)type.GetField("lastAcceptedPosition", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(navigator)!);
                type.GetMethod("EndDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, null);
            }
            Assert.Equal(0d, (double)getPosition.Invoke(navigator, [-100d])!);
            Assert.Equal(duration, (double)getPosition.Invoke(navigator, [width + 100])!);
            var getTrackPosition = type.GetMethod("GetPositionForTrackX", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(0d, (double)getTrackPosition.Invoke(navigator, [0d])!);
            Assert.Equal(duration / 2, (double)getTrackPosition.Invoke(navigator, [width / 2])!, 6);
            Assert.Equal(duration, (double)getTrackPosition.Invoke(navigator, [width])!);
        });
    }

    internal static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Drag_follows_pointer_in_both_directions_without_elapsed_time_capping()
    {
        OnSta(() =>
        {
            var navigator = new ReviewTimelineNavigator { DurationSeconds = 100, PositionSeconds = 50 };
            navigator.Measure(new Size(640, 30));
            navigator.Arrange(new Rect(0, 0, 640, 30));
            var type = typeof(ReviewTimelineNavigator);
            var thumb = (Rect)type.GetMethod("GetThumbRect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, null)!;
            var started = 0;
            var ended = 0;
            var positions = new List<double>();
            navigator.DragStarted += (_, _) => started++;
            navigator.DragCompleted += (_, _) => ended++;
            navigator.PreviewPositionChanged += (_, value) => positions.Add(value);
            type.GetMethod("BeginDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(navigator, [new Point(thumb.Left + 9, thumb.Top), thumb]);
            foreach (var seconds in new[] { 90d, 10, 75, 25 })
            {
                var point = new Point(seconds / 100 * (640 - 18) + 9, thumb.Top);
                type.GetMethod("ContinueDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, [point]);
                var currentThumb = (Rect)type.GetMethod("GetThumbRect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, null)!;
                Assert.Equal(point.X, currentThumb.Left + 9, 6);
            }
            Assert.Equal(4, positions.Count);
            Assert.All(positions.Zip(new[] { 90d, 10, 75, 25 }), pair => Assert.Equal(pair.Second, pair.First, 6));
            type.GetMethod("EndDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, null);
            type.GetMethod("EndDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigator, null);
            Assert.Equal(1, started);
            Assert.Equal(1, ended);
            Assert.Null(type.GetMethod("SetCursorPos", BindingFlags.Static | BindingFlags.NonPublic));
        });
    }
}
