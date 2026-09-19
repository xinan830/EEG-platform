using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_RoutesBusinessFailureToTheRequiredLocalErrorHandler()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            () => Task.FromException(new InvalidOperationException("配置名称不能为空。")),
            exception => reported.TrySetResult(exception));

        command.Execute(null);

        var error = await reported.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("配置名称不能为空。", error.Message);
    }
}
