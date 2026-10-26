using BrainPlatform.Desktop.Modules.Algorithms.Api;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class DynamicWindowObserverTests
{
    [Theory]
    [InlineData("queued", 0, 0, 100)]
    [InlineData("preparing_source", 0, 0, 100)]
    [InlineData("computing_window", 0, 0, 100)]
    [InlineData("queued", 999, 999, 100)]
    [InlineData("preparing_source", 999, 999, 100)]
    [InlineData("computing_window", 999, 999, 100)]
    [InlineData("queued", 1000, 1000, 500)]
    [InlineData("preparing_source", 1000, 1000, 250)]
    [InlineData("computing_window", 1000, 1000, 150)]
    [InlineData("queued", 5000, 5000, 750)]
    [InlineData("preparing_source", 5000, 5000, 750)]
    [InlineData("computing_window", 5000, 5000, 750)]
    [InlineData("preparing_source", 5000, 2999, 250)]
    [InlineData("computing_window", 5000, 3000, 750)]
    public void Short_jobs_are_discovered_promptly_and_long_jobs_back_off(
        string state, int elapsedMilliseconds, int stateElapsedMilliseconds, int expectedMilliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds),
            DynamicWindowObserver.PollDelay(state, TimeSpan.FromMilliseconds(elapsedMilliseconds),
                TimeSpan.FromMilliseconds(stateElapsedMilliseconds)));
    }
}
