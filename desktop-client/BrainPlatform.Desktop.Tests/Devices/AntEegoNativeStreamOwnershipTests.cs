using BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class AntEegoNativeStreamOwnershipTests
{
    [Fact]
    public async Task NativeOperationGate_WaitsForReadToExitBeforeCloseRuns()
    {
        var gate = new AntEegoNativeOperationGate();
        using var readEntered = new ManualResetEventSlim();
        using var finishRead = new ManualResetEventSlim();
        using var closeEntered = new ManualResetEventSlim();
        var read = Task.Run(() => gate.Invoke(() =>
        {
            readEntered.Set();
            finishRead.Wait();
        }));
        Assert.True(readEntered.Wait(TimeSpan.FromSeconds(2)));
        var close = Task.Run(() => gate.Invoke(closeEntered.Set));

        Assert.False(closeEntered.Wait(TimeSpan.FromMilliseconds(50)));
        finishRead.Set();
        await Task.WhenAll(read, close);
        Assert.True(closeEntered.IsSet);
    }

    [Fact]
    public async Task StreamGate_AllowsOnlyOneConcurrentOwner()
    {
        var gate = new AntEegoNativeStreamGate();
        gate.Acquire();
        var rejected = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Assert.Throws<AntEegoSdkException>(gate.Acquire))));

        Assert.All(rejected, exception => Assert.Equal("ANT_EEGO_STREAM_ALREADY_ACTIVE", exception.Code));
        Assert.True(gate.IsOccupied);
        gate.Release();
        gate.Acquire();
        gate.Release();
    }

    [Fact]
    public async Task FailedNativeClose_KeepsGlobalStreamGateOccupiedUntilRetrySucceeds()
    {
        var gate = new AntEegoNativeStreamGate();
        gate.Acquire();
        var attempts = 0;
        var ownership = new AntEegoNativeStreamOwnership(
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new IOException("native close has not completed");
            },
            () => { },
            gate.Release,
            () => { });

        await Assert.ThrowsAsync<IOException>(async () => await ownership.DisposeAsync());
        Assert.Throws<AntEegoSdkException>(gate.Acquire);
        await ownership.DisposeAsync();
        gate.Acquire();
        gate.Release();
    }

    [Fact]
    public async Task Dispose_ClosesStreamThenAmplifierThenLease_AndIsIdempotent()
    {
        var calls = new List<string>();
        var ownership = CreateOwnership(calls, () => { });

        await ownership.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Equal(["stream", "amplifier", "lease", "released"], calls);
    }

    [Fact]
    public async Task Dispose_StreamCloseFailureRetainsOwnershipAndCanRetry()
    {
        var calls = new List<string>();
        var attempts = 0;
        var ownership = CreateOwnership(calls, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new IOException("close failed");
        });

        await Assert.ThrowsAsync<IOException>(async () => await ownership.DisposeAsync());
        Assert.False(ownership.IsReleased);
        Assert.Equal(["stream"], calls);

        await ownership.DisposeAsync();

        Assert.Equal(["stream", "stream", "amplifier", "lease", "released"], calls);
    }

    [Fact]
    public async Task Dispose_AmplifierCloseFailureDoesNotRecloseStreamOrReleaseLease()
    {
        var calls = new List<string>();
        var attempts = 0;
        var ownership = new AntEegoNativeStreamOwnership(
            () => calls.Add("stream"),
            () =>
            {
                calls.Add("amplifier");
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new IOException("close failed");
            },
            () => calls.Add("lease"),
            () => calls.Add("released"));

        await Assert.ThrowsAsync<IOException>(async () => await ownership.DisposeAsync());
        await ownership.DisposeAsync();

        Assert.Equal(["stream", "amplifier", "amplifier", "lease", "released"], calls);
    }

    private static AntEegoNativeStreamOwnership CreateOwnership(List<string> calls, Action closeStream) => new(
        () =>
        {
            calls.Add("stream");
            closeStream();
        },
        () => calls.Add("amplifier"),
        () => calls.Add("lease"),
        () => calls.Add("released"));
}
