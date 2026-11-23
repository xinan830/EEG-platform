using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class AntEegoImpedanceNativeBoundaryTests
{
    [Fact]
    public async Task NativeBoundaryPreservesReturnedOrderAndOhms_AndPacesRepeatedStates()
    {
        var calls = new List<string>();
        var values = new[] { 4500d, 2500d, 9000d };
        var native = new Substitute(calls, values);
        using var api = new AntEegoNativeApi(0, native.Bind);
        var request = new[] { new ImpedanceChannelIdentity(4, AcquisitionChannelKind.Reference) };
        Assert.Equal(17, api.OpenImpedanceStream(3, AntEegoImpedanceContractMapper.ToNativeRequest(request)));
        var metadata = AntEegoImpedanceContractMapper.CreateMetadata("ant-eego:3", "Fake", request,
            api.GetStreamChannels(17), new Dictionary<int, string> { [4] = "Fz", [0] = "WrongLabel" });
        var lease = new AntEegoNativeLease(api, true, _ => calls.Add("lease"));
        var ownership = new AntEegoNativeStreamOwnership(() => lease.Invoke(value => value.CloseStream(17)),
            () => lease.Invoke(value => value.CloseAmplifier(3)), lease.Dispose, () => calls.Add("released"));
        await using var stream = new AntEegoImpedanceStream(lease, Guid.NewGuid(), 17, metadata, ownership);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var enumerator = stream.ReadSnapshotsAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(["REF", "Fz", "GND"], enumerator.Current.Readings.Select(reading => reading.Label));
        Assert.Equal(values, enumerator.Current.Readings.Select(reading => reading.ResistanceOhms!.Value));
        var clock = Stopwatch.StartNew();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(clock.ElapsedMilliseconds >= 400, "Successful impedance reads must not busy-poll.");
        Assert.Equal(2, enumerator.Current.UpdateSequence);
        await stream.DisposeAsync();
        Assert.Equal(["stream", "amplifier", "lease", "released"], calls);
        Assert.Throws<ObjectDisposedException>(() => lease.Invoke(value => value.Prefetch(17)));
    }

    [Fact]
    public async Task NativeBoundaryRejectsMultipleSamplesInsteadOfDecodingThemAsElectrodes()
    {
        var native = new Substitute([], [1d, 2d, 3d, 4d]);
        using var api = new AntEegoNativeApi(0, native.Bind);
        var lease = new AntEegoNativeLease(api, true, _ => { });
        var ownership = new AntEegoNativeStreamOwnership(() => { }, () => { }, lease.Dispose, () => { });
        await using var stream = new AntEegoImpedanceStream(lease, Guid.NewGuid(), 17,
            new("fake", "Fake", [new(0, 4, AcquisitionChannelKind.Reference, "Fz")]), ownership);
        await using var reader = stream.ReadSnapshotsAsync(default).GetAsyncEnumerator();
        var exception = await Assert.ThrowsAsync<AntEegoSdkException>(async () => await reader.MoveNextAsync());
        Assert.Equal("ANT_EEGO_IMPEDANCE_DATA_SHAPE_INVALID", exception.Code);
    }

    [Fact]
    public void MissingOptionalExportIsUnsupported_AndGenericNativeErrorDoesNotBecomeUnsupported()
    {
        var missing = new Substitute([], [1d]) { MissingExport = true };
        using var api = new AntEegoNativeApi(0, missing.Bind);
        Assert.False(api.SupportsImpedance);
        var failing = new Substitute([], [1d]) { OpenResult = -5 };
        using var failingApi = new AntEegoNativeApi(0, failing.Bind);
        var exception = Assert.Throws<AntEegoSdkException>(() => failingApi.OpenImpedanceStream(3, []));
        Assert.Equal("ANT_EEGO_NATIVE_-5", exception.Code);
    }

    [Fact]
    public void UnsupportedPhysicalKindsFailBeforeNativeSelection() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AntEegoImpedanceContractMapper.ToNativeRequest([new(0, AcquisitionChannelKind.Bipolar)]));

    [Fact]
    public async Task CancellationAfterNativeOpenClosesInOrderWithoutPublishingAStream()
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var native = new Substitute(calls, [1d]) { OnOpen = cancellation.Cancel };
        using var api = new AntEegoNativeApi(0, native.Bind);
        var lease = new AntEegoNativeLease(api, true, _ => calls.Add("lease"));
        await using var adapter = new AntEegoAcquisitionAdapter(new("fake.dll", null, null), (_, _) => lease);
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.OpenImpedanceStreamAsync(
            new(Guid.NewGuid(), "ant-eego:3", [new(4, AcquisitionChannelKind.Reference)]), cancellation.Token));
        Assert.Equal(["stream", "amplifier", "lease"], calls);
        Assert.Equal(ImpedanceCapabilityStatus.Unknown, adapter.ImpedanceCapability);
    }

    [Fact]
    public async Task PartialOpenCleanupFailureBlocksNewStreamsAndRetainsLeaseUntilRetry()
    {
        var calls = new List<string>();
        var native = new Substitute(calls, [1d]) { InvalidChannelCount = true, FailClose = true };
        using var api = new AntEegoNativeApi(0, native.Bind);
        var lease = new AntEegoNativeLease(api, true, _ => calls.Add("lease"));
        await using var adapter = new AntEegoAcquisitionAdapter(new("fake.dll", null, null), (_, _) => lease);
        var request = new ImpedanceStreamRequest(Guid.NewGuid(), "ant-eego:3", [new(4, AcquisitionChannelKind.Reference)]);
        var error = await Assert.ThrowsAsync<AntEegoSdkException>(() => adapter.OpenImpedanceStreamAsync(request, default));
        Assert.Equal("ANT_EEGO_OPEN_CLEANUP_FAILED", error.Code);
        Assert.DoesNotContain("lease", calls);
        await Assert.ThrowsAsync<AntEegoSdkException>(() => adapter.OpenImpedanceStreamAsync(request, default));
        native.FailClose = false;
        await adapter.DisposeAsync();
        Assert.Equal(["stream", "stream", "amplifier", "lease"], calls);
    }

    private sealed class Substitute(List<string> calls, double[] values)
    {
        public bool MissingExport { get; init; }
        public int OpenResult { get; init; } = 17;
        public bool InvalidChannelCount { get; init; }
        public bool FailClose { get; set; }
        public Action? OnOpen { get; init; }
        public Delegate? Bind(string name, Type delegateType)
        {
            if (name == "eemagine_sdk_open_impedance_stream" && MissingExport) return null;
            var signature = delegateType.GetMethod("Invoke")!;
            var parameters = signature.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType)).ToArray();
            var dispatch = Expression.Call(Expression.Constant(this), nameof(Dispatch), null, Expression.Constant(name),
                Expression.NewArrayInit(typeof(object), parameters.Select(parameter => Expression.Convert(parameter, typeof(object)))));
            Expression body = signature.ReturnType == typeof(void)
                ? Expression.Block(dispatch, Expression.Empty()) : dispatch;
            return Expression.Lambda(delegateType, body, parameters).Compile();
        }
        public int Dispatch(string name, object[] arguments)
        {
            switch (name)
            {
                case "eemagine_sdk_open_impedance_stream":
                    if (OpenResult >= 0)
                    {
                        var channel = Assert.Single((AntEegoNativeChannelInfo[])arguments[1]);
                        Assert.Equal(4, channel.Index);
                        Assert.Equal(AntEegoNativeChannelType.Reference, channel.Type);
                    }
                    OnOpen?.Invoke();
                    return OpenResult;
                case "eemagine_sdk_get_amplifiers_info":
                    ((AntEegoNativeAmplifierInfo[])arguments[0])[0] = new() { Id = 3, Serial = "FAKE" };
                    return 1;
                case "eemagine_sdk_get_stream_channel_count": return InvalidChannelCount ? 4 : 3;
                case "eemagine_sdk_get_stream_channel_list":
                    var channels = (AntEegoNativeChannelInfo[])arguments[1];
                    channels[0] = new() { Index = 0, Type = AntEegoNativeChannelType.ImpedanceReference };
                    channels[1] = new() { Index = 4, Type = AntEegoNativeChannelType.Reference };
                    channels[2] = new() { Index = 0, Type = AntEegoNativeChannelType.ImpedanceGround };
                    return 3;
                case "eemagine_sdk_prefetch": return values.Length * sizeof(double);
                case "eemagine_sdk_get_data": Marshal.Copy(values, 0, (nint)arguments[1], values.Length); return 0;
                case "eemagine_sdk_close_stream": calls.Add("stream"); return FailClose ? -5 : 0;
                case "eemagine_sdk_close_amplifier": calls.Add("amplifier"); return 0;
                case "eemagine_sdk_get_error_string": Marshal.WriteByte((nint)arguments[0], 0); return 0;
                default: return 0;
            }
        }
    }
}
