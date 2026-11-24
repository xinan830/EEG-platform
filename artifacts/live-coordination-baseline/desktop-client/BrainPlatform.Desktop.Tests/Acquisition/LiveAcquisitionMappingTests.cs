using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveAcquisitionMappingTests
{
    [Theory]
    [InlineData("F88")]
    [InlineData("F7")]
    public void Lifecycle_PreservesSelectedNativeMappingAndTraceValues(string electrode)
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.ApplyMontage(electrode);
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.AssertMapping(electrode);

            probe.Session.ConfigureForStreamAsync(probe.Configuration(), CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            probe.AssertMapping(electrode);
            Assert.Equal(500, probe.Workspace.SelectedSamplingRateHz);

            probe.Runtime.StartPreviewAsync(new AcquisitionStreamRequest(
                "probe:1", 1000, probe.Project), CancellationToken.None).GetAwaiter().GetResult();
            WaitForSamples(probe.Runtime);
            probe.AssertTraceValues(electrode);

            probe.Runtime.StartRecordingAsync(CancellationToken.None).GetAwaiter().GetResult();
            probe.AssertTraceValues(electrode);
            probe.Runtime.PauseAsync(CancellationToken.None).GetAwaiter().GetResult();
            probe.AssertTraceValues(electrode);
            probe.Runtime.ResumeAsync(CancellationToken.None).GetAwaiter().GetResult();
            probe.AssertTraceValues(electrode);
            probe.Runtime.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            probe.AssertMapping(electrode);
        });
    }

    [Fact]
    public void EquivalentRediscovery_DoesNotDiscardSelectedConfiguration()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.ApplyMontage("F88");
            probe.Driver.Device = probe.Driver.Device with
            {
                SupportedSamplingRatesHz = [1000, 500],
                ChannelCapabilities = probe.Driver.Device.ChannelCapabilities!.Reverse().ToArray(),
            };
            probe.Session.RefreshAvailabilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            probe.AssertMapping("F88");
        });
    }

    [Fact]
    public void ChangedCapabilities_RefreshDeviceBackedSetup()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.ApplyMontage("F88");
            probe.Driver.Device = probe.Driver.Device with
            {
                ChannelCapabilities = probe.Driver.Device.ChannelCapabilities!
                    .Where(channel => channel.NativeChannelIndex != 5).ToArray(),
            };
            probe.Session.RefreshAvailabilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            Assert.Null(probe.Workspace.ChannelMapping.ActiveConfiguration);
            Assert.DoesNotContain(probe.Workspace.ChannelMapping.Rows, row => row.NativeChannelIndex == 5);
        });
    }

    [Fact]
    public void ReplacedPhysicalDevice_RefreshesDeviceBackedSetup()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.ApplyMontage("F88");
            probe.Driver.Device = probe.Driver.Device with { DeviceId = "probe:2", SerialNumber = "replacement" };
            probe.Session.RefreshAvailabilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            Assert.Equal("probe:2", probe.Workspace.SelectedDevice?.DeviceId);
            Assert.Null(probe.Workspace.ChannelMapping.ActiveConfiguration);
            Assert.Equal(28, probe.Workspace.ChannelMapping.Rows.Count);
        });
    }

    [Fact]
    public void DisconnectAndReconnect_ClearThenReloadDeviceSetup()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.ApplyMontage("F88");
            probe.Driver.IsPresent = false;
            probe.Session.RefreshAvailabilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            Assert.Empty(probe.Workspace.ChannelMapping.Rows);
            Assert.Null(probe.Workspace.ChannelMapping.ActiveConfiguration);

            probe.Driver.IsPresent = true;
            probe.Session.RefreshAvailabilityAsync(CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            Assert.Equal(28, probe.Workspace.ChannelMapping.Rows.Count);
        });
    }

    private static void RunOnDispatcher(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        thread.Join();
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitForSamples(ConfiguredAcquisitionRuntime runtime)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (runtime.GetDisplaySnapshot().Count == 0 && Environment.TickCount64 < deadline)
        {
            DrainDispatcher();
            Thread.Sleep(10);
        }
        Assert.NotEmpty(runtime.GetDisplaySnapshot());
    }

    private sealed class ProbeSession : IDisposable
    {
        public ProbeDriver Driver { get; } = new();
        public ConfiguredAcquisitionRuntime Runtime { get; }
        public DeviceSessionManager Session { get; }
        public AcquisitionWorkspaceViewModel Workspace { get; }
        public AcquisitionProjectContext Project { get; } = new(
            "probe", "probe", "Synthetic mapping test",
            Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N")), "{}");

        public ProbeSession()
        {
            Runtime = new ConfiguredAcquisitionRuntime(driverRegistry: new AcquisitionDriverRegistry([Driver]));
            Session = new DeviceSessionManager(Runtime);
            Workspace = new AcquisitionWorkspaceViewModel(new ProbeSettingsStore(), Runtime, Session);
            Session.DiscoverAsync(Configuration(), CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
        }

        public AcquisitionDriverConfiguration Configuration() => new(
            AntEegoAcquisitionDriver.DriverId, new Dictionary<string, string>(),
            Workspace.ChannelMapping.GetCurrentMapping());

        public void ApplyMontage(string electrode)
        {
            var mapping = Workspace.ChannelMapping;
            Assert.True(DefaultChannelLabelTemplate.TryGetFor(Driver.Device.ChannelCapabilities!, out var labels));
            var entries = mapping.CaptureConfigurationEntries().Select(entry => entry with
            {
                ElectrodeLabel = entry.NativeChannelIndex switch
                {
                    3 => electrode,
                    5 => "F3",
                    2 when electrode == "F7" => "F77",
                    _ => labels.GetValueOrDefault(entry.NativeChannelIndex, ""),
                },
                IsSelectedForDisplay = entry.NativeChannelIndex is 0 or 1 or 3 or 5,
            }).ToArray();
            var snapshot = new ChannelConfigurationProfile(
                "probe", "Probe", "", ChannelConfigurationSource.User, mapping.GetDeviceSignature(),
                entries, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            ChannelConfigurationValidation.Validate(snapshot);
            var outputs = entries.Where(entry => entry.IsSelectedForDisplay).Select(entry =>
                new DerivedMontageChannel($"{entry.ElectrodeLabel}-REF", entry.ElectrodeLabel,
                    MontageNegativeKind.OriginalHardwareReference, [], entry.DisplayOrder)).ToArray();
            var fingerprint = ChannelConfigurationFingerprint.Create(snapshot);
            mapping.ApplyConfiguration(snapshot);
            Workspace.LiveMonitor.ConfigureMontage(new MontageProfile(
                "probe-montage", "Probe", "", MontageProfileSource.User,
                snapshot.DeviceSignature, fingerprint, snapshot, outputs,
                1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                MontageProfileFingerprint.Create(fingerprint, outputs)));
            DrainDispatcher();
        }

        public void AssertMapping(string electrode)
        {
            DrainDispatcher();
            Assert.Equal(electrode, Workspace.ChannelMapping.GetCurrentMapping()[3]);
            Assert.Equal("F3", Workspace.ChannelMapping.GetCurrentMapping()[5]);
            Assert.Equal("probe", Workspace.ChannelMapping.ActiveConfigurationId);
        }

        public void AssertTraceValues(string electrode)
        {
            AssertMapping(electrode);
            Workspace.LiveMonitor.Refresh();
            var source = Assert.IsType<LiveWaveformSource>(Workspace.LiveMonitor.GetWaveformSource());
            Assert.Equal(electrode, source.Metadata.Channels.Single(channel => channel.NativeChannelIndex == 3).Label);
            var frame = Assert.IsType<WaveformDisplayFrame>(WaveformDisplayFrameBuilder.Build(source, 1, 1000));
            Assert.Equal(["Fp1-REF", "Fp2-REF", $"{electrode}-REF", "F3-REF"], frame.Traces.Select(trace => trace.Label));
            Assert.Equal(40e-6, frame.Traces.Single(trace => trace.Label == $"{electrode}-REF").Points[0].MinVolts, 12);
            Assert.Equal(60e-6, frame.Traces.Single(trace => trace.Label == "F3-REF").Points[0].MinVolts, 12);
            Assert.Equal(20e-6, source.Batches[0].SampleMajorValues[2], 12);
            Assert.Equal(40e-6, source.Batches[0].SampleMajorValues[3], 12);
            Assert.Equal(60e-6, source.Batches[0].SampleMajorValues[5], 12);
        }

        public void Dispose()
        {
            Workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            DrainDispatcher();
            if (Directory.Exists(Project.DirectoryPath))
                Directory.Delete(Project.DirectoryPath, recursive: true);
        }
    }

    private sealed class ProbeSettingsStore : IAcquisitionSettingsStore
    {
        public AcquisitionConnectionSettings Load() => AcquisitionConnectionSettings.CreateDefault() with
        {
            SdkLibraryPath = "",
            RecordingDirectory = Path.Combine(Path.GetTempPath(), "unused-mapping-test"),
        };
        public Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Test must not save user settings.");
    }

    private sealed class ProbeDriver : IAcquisitionDeviceDriver
    {
        public AcquisitionDriverDescriptor Descriptor { get; } = new(
            AntEegoAcquisitionDriver.DriverId, "Synthetic ANT layout", "No SDK or hardware");
        public bool IsPresent { get; set; } = true;
        public AcquisitionDeviceDescriptor Device { get; set; } = new(
            "probe:1", "Synthetic ANT layout", null, [500, 1000],
            ChannelCapabilities: Enumerable.Range(0, 30).Select(index =>
                new AcquisitionChannelCapability(index, index < 24 ? AcquisitionChannelKind.Reference
                    : index < 28 ? AcquisitionChannelKind.Bipolar
                    : index == 28 ? AcquisitionChannelKind.Trigger : AcquisitionChannelKind.SampleCounter,
                    index < 28 ? "V" : index == 28 ? "code" : "count")).ToArray());
        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) =>
            new ProbeAdapter(this, configuration.ChannelLabelsByNativeIndex);
    }

    private sealed class ProbeAdapter(ProbeDriver driver, IReadOnlyDictionary<int, string>? labels) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Synthetic only");
        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>(driver.IsPresent ? [driver.Device] : []);
        public Task<IAcquisitionStream> OpenEegStreamAsync(AcquisitionStreamRequest request,
            CancellationToken cancellationToken) => Task.FromResult<IAcquisitionStream>(new ProbeStream(driver.Device, labels));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProbeStream(AcquisitionDeviceDescriptor device, IReadOnlyDictionary<int, string>? labels) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new("probe:1", "Synthetic ANT layout", 1000,
            device.ChannelCapabilities!.Select((channel, index) => new AcquisitionChannel(index,
                channel.NativeChannelIndex, labels?.GetValueOrDefault(channel.NativeChannelIndex),
                channel.Kind, channel.Unit)).ToArray(), 29, DateTimeOffset.UtcNow);

        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (long first = 0; ; first += 2)
            {
                var values = Enumerable.Range(0, 60).Select(offset => (offset % 30) switch
                {
                    29 => (double)(first + offset / 30),
                    2 => 20e-6, 3 => 40e-6, 5 => 60e-6, _ => 1e-6,
                }).ToArray();
                yield return new AcquisitionBatch(first, 2, 30, values, DateTimeOffset.UtcNow);
                await Task.Delay(10, cancellationToken);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
