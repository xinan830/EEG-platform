using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Acquisition.Session;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Acquisition.Waveform;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Validation;
using BrainPlatform.Desktop.Modules.Devices.Contracts;
using BrainPlatform.Desktop.Modules.Devices.Domain;
using BrainPlatform.Desktop.Modules.Devices.Drivers;
using BrainPlatform.Desktop.Modules.Devices.Drivers.AntEego;
using BrainPlatform.Desktop.Modules.Montages.Domain;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Test-only hardware; no SDK, raw writer, settings writes or live device.
        RunCase("F88");
        RunCase("F7");
        return 0;
    }

    private static void RunCase(string targetLabel)
    {
        Console.WriteLine($"CASE CH3 -> {targetLabel}");
        var driver = new ProbeDriver();
        var runtime = new ConfiguredAcquisitionRuntime(
            driverRegistry: new AcquisitionDriverRegistry([driver]));
        var session = new DeviceSessionManager(runtime);
        var workspace = new AcquisitionWorkspaceViewModel(
            new ProbeSettingsStore(), runtime, session, Dispatcher.CurrentDispatcher);
        try
        {
            session.DiscoverAsync(new AcquisitionDriverConfiguration(
                AntEegoAcquisitionDriver.DriverId, new Dictionary<string, string>()),
                CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            var mapping = workspace.ChannelMapping;
            var entries = mapping.CaptureConfigurationEntries().Select(entry => entry with
            {
                ElectrodeLabel = entry.NativeChannelIndex == 3
                    ? targetLabel
                    : entry.NativeChannelIndex == 2 && targetLabel == "F7"
                        ? "F77"
                        : entry.ElectrodeLabel,
                IsSelectedForDisplay = entry.NativeChannelIndex is 0 or 1 or 3,
            }).ToArray();
            var snapshot = new ChannelConfigurationProfile(
                "probe", "Probe", "Synthetic inputs", ChannelConfigurationSource.User,
                mapping.GetDeviceSignature(), entries, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            ChannelConfigurationValidation.Validate(snapshot);
            var definitions = entries.Where(entry => entry.IsSelectedForDisplay)
                .Select(entry => new DerivedMontageChannel(
                    $"{entry.ElectrodeLabel}-REF", entry.ElectrodeLabel,
                    MontageNegativeKind.OriginalHardwareReference, [], entry.DisplayOrder)).ToArray();
            var fingerprint = ChannelConfigurationFingerprint.Create(snapshot);
            var montage = new MontageProfile(
                "probe-montage", "Probe montage", "", MontageProfileSource.User,
                snapshot.DeviceSignature, fingerprint, snapshot, definitions,
                1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                MontageProfileFingerprint.Create(fingerprint, definitions));

            mapping.ApplyConfiguration(snapshot);
            workspace.LiveMonitor.ConfigureMontage(montage);
            DrainDispatcher();
            PrintMapping("after_apply", mapping.GetCurrentMapping());
            var configuration = AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(
                "synthetic-sdk", 1, 1, ChannelLabelsByNativeIndex: mapping.GetCurrentMapping()));
            session.ConfigureForStreamAsync(configuration, CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
            PrintMapping("after_prepare_state_notification", mapping.GetCurrentMapping());

            var project = new AcquisitionProjectContext(
                "probe", "probe", "probe", Path.GetTempPath(), "{}");
            runtime.StartPreviewAsync(new AcquisitionStreamRequest("probe:1", 1000, project),
                CancellationToken.None).GetAwaiter().GetResult();
            var deadline = Environment.TickCount64 + 3000;
            while (runtime.GetDisplaySnapshot().Count == 0 && Environment.TickCount64 < deadline)
            {
                DrainDispatcher();
                Thread.Sleep(10);
            }
            DrainDispatcher();
            workspace.LiveMonitor.Refresh();
            var source = workspace.LiveMonitor.GetWaveformSource()
                ?? throw new InvalidOperationException("Synthetic stream metadata is absent.");
            var frame = WaveformDisplayFrameBuilder.Build(source, 1, 1000)
                ?? throw new InvalidOperationException("Synthetic display frame is absent.");
            Console.WriteLine($"stream_metadata_CH3={source.Metadata.Channels.Single(channel => channel.NativeChannelIndex == 3).Label}");
            Console.WriteLine($"live_source_CH3={source.MontageSourceChannels!.Single(channel => channel.NativeChannelIndex == 3).Label}");
            Console.WriteLine($"actual_traces={string.Join(',', frame.Traces.Select(trace => trace.Label))}");
            PrintTarget("actual", frame, targetLabel);

            // Control uses the same bytes/stream indexes with the selected snapshot.
            var selectedSources = source.Metadata.Channels
                .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
                .Select(channel => new LiveDisplayChannel(
                    channel.StreamIndex, channel.NativeChannelIndex, channel.Label ?? "",
                    channel.Kind.ToString(), entries.Single(entry => entry.NativeChannelIndex == channel.NativeChannelIndex).IsSelectedForDisplay))
                .ToArray();
            var control = WaveformDisplayFrameBuilder.Build(source with
            {
                Channels = selectedSources.Where(channel => channel.IsVisible).ToArray(),
                MontageSourceChannels = selectedSources,
            }, 1, 1000) ?? throw new InvalidOperationException("Control frame is absent.");
            Console.WriteLine($"control_traces={string.Join(',', control.Traces.Select(trace => trace.Label))}");
            PrintTarget("control", control, targetLabel);
            Console.WriteLine($"raw_CH2_first_uV={source.Batches[0].SampleMajorValues[2] * 1e6:G6}");
            Console.WriteLine($"raw_CH3_first_uV={source.Batches[0].SampleMajorValues[3] * 1e6:G6}");

            if (targetLabel == "F7")
            {
                try
                {
                    ChannelConfigurationValidation.Validate(snapshot with
                    {
                        Channels = entries.Select(entry => entry.NativeChannelIndex == 2
                            ? entry with { ElectrodeLabel = "F7" }
                            : entry).ToArray(),
                    });
                    throw new InvalidOperationException("Duplicate-label validation unexpectedly passed.");
                }
                catch (InvalidOperationException exception) when (exception.Message.Contains("F7") &&
                    !exception.Message.Contains("unexpectedly"))
                {
                    Console.WriteLine($"duplicate_F7_rejected={exception.Message}");
                }
            }
        }
        finally
        {
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            DrainDispatcher();
        }
    }

    private static void PrintMapping(string stage, IReadOnlyDictionary<int, string> mapping) =>
        Console.WriteLine($"{stage}_CH3={mapping.GetValueOrDefault(3, "<absent>")}");

    private static void PrintTarget(string stage, WaveformDisplayFrame frame, string label)
    {
        var trace = frame.Traces.SingleOrDefault(trace => trace.Label == $"{label}-REF");
        Console.WriteLine($"{stage}_{label}_first_uV={(trace is { Points.Count: > 0 } ? (trace.Points[0].MinVolts * 1e6).ToString("G6") : "<missing>")}");
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class ProbeSettingsStore : IAcquisitionSettingsStore
    {
        public AcquisitionConnectionSettings Load() => AcquisitionConnectionSettings.CreateDefault() with
        {
            SdkLibraryPath = "",
            RecordingDirectory = Path.Combine(Path.GetTempPath(), "unused-mapping-probe"),
        };

        public Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Diagnostic must not save settings.");
    }

    private sealed class ProbeDriver : IAcquisitionDeviceDriver
    {
        public AcquisitionDriverDescriptor Descriptor { get; } = new(
            AntEegoAcquisitionDriver.DriverId, "Synthetic ANT-layout driver", "No hardware access");

        public static AcquisitionDeviceDescriptor Device { get; } = new(
            "probe:1", "Synthetic ANT layout", null, [1000],
            ChannelCapabilities: Enumerable.Range(0, 30).Select(index =>
                new AcquisitionChannelCapability(index, index < 24 ? AcquisitionChannelKind.Reference
                    : index < 28 ? AcquisitionChannelKind.Bipolar
                    : index == 28 ? AcquisitionChannelKind.Trigger : AcquisitionChannelKind.SampleCounter,
                    index < 28 ? "V" : index == 28 ? "code" : "count")).ToArray());

        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) =>
            new ProbeAdapter(configuration.ChannelLabelsByNativeIndex);
    }

    private sealed class ProbeAdapter(IReadOnlyDictionary<int, string>? labels) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Synthetic only");
        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>([ProbeDriver.Device]);
        public Task<IAcquisitionStream> OpenEegStreamAsync(AcquisitionStreamRequest request,
            CancellationToken cancellationToken) => Task.FromResult<IAcquisitionStream>(new ProbeStream(labels));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProbeStream(IReadOnlyDictionary<int, string>? labels) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new(
            "probe:1", "Synthetic ANT layout", 1000,
            ProbeDriver.Device.ChannelCapabilities!.Select((capability, index) => new AcquisitionChannel(
                index, capability.NativeChannelIndex, labels?.GetValueOrDefault(capability.NativeChannelIndex),
                capability.Kind, capability.Unit)).ToArray(), 29, DateTimeOffset.UtcNow);

        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var values = Enumerable.Range(0, 60).Select(offset => offset % 30 == 29
                ? (double)(offset / 30)
                : offset % 30 == 2 ? 20e-6 : offset % 30 == 3 ? 40e-6 : 1e-6).ToArray();
            yield return new AcquisitionBatch(0, 2, 30, values, DateTimeOffset.UtcNow);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
