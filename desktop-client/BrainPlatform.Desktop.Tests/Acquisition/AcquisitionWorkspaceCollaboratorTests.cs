using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Events.Services;
using BrainPlatform.Desktop.Modules.Events.Stores;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionWorkspaceCollaboratorTests
{
    [Fact]
    public void InputRangeSelection_AutoSelectsRestoresAndSupportsManualEdit()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession(
                referenceRanges: [0.5, 1.0],
                bipolarRanges: [1.25, 2.5],
                savedSamplingRateHz: 1000,
                savedReferenceRangeText: "1",
                savedBipolarRangeText: "2.5");

            Assert.Equal(1000, probe.Workspace.SelectedSamplingRateHz);
            Assert.Equal(1.0, probe.Workspace.SelectedReferenceRangeVolts);
            Assert.Equal(2.5, probe.Workspace.SelectedBipolarRangeVolts);
            Assert.Equal("自动选择", probe.Workspace.InputRangeSelectionModeText);
            Assert.True(probe.Workspace.CanEditInputRanges);

            probe.Workspace.BeginInputRangeEditCommand.Execute(null);
            Assert.True(probe.Workspace.IsEditingInputRanges);

            probe.Workspace.SelectedReferenceRangeVolts = 0.5;
            Assert.Equal([1.25], probe.Workspace.BipolarRangesVolts);
            Assert.Equal(1.25, probe.Workspace.SelectedBipolarRangeVolts);
            Assert.True(probe.Workspace.CanConfirmInputRangeEdit);

            probe.Workspace.ConfirmInputRangeEditCommand.Execute(null);
            Assert.False(probe.Workspace.IsEditingInputRanges);
            Assert.Equal("手动设置", probe.Workspace.InputRangeSelectionModeText);

            probe.Workspace.CancelInputRangeEditCommand.Execute(null);
            Assert.Equal(1.0, probe.Workspace.SelectedReferenceRangeVolts);
            Assert.Equal(2.5, probe.Workspace.SelectedBipolarRangeVolts);
            Assert.Equal("自动选择", probe.Workspace.InputRangeSelectionModeText);

            // Incompatible pair must be rejected without switching to manual mode.
            probe.Workspace.BeginInputRangeEditCommand.Execute(null);
            probe.Workspace.SelectedReferenceRangeVolts = 1.0;
            Assert.False(probe.Workspace.CanConfirmInputRangeEdit && probe.Workspace.SelectedBipolarRangeVolts == 1.25);
            probe.Workspace.SelectedBipolarRangeVolts = 1.25;
            Assert.False(probe.Workspace.CanConfirmInputRangeEdit);
            probe.Workspace.ConfirmInputRangeEditCommand.Execute(null);
            DrainDispatcher();
            Assert.True(probe.Workspace.IsEditingInputRanges);
            Assert.Contains("兼容", probe.Workspace.OperationMessage);
        });
    }

    [Fact]
    public void StartPreparedPreview_ValidationFailureDoesNotMutateStateOrPersistSettings()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            var montage = probe.CreateMontage("F88");
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = montage;
            probe.Workspace.SelectedSamplingRateHz = 500;
            // Nyquist violation: low-pass >= 500 / 2 (250 Hz)
            probe.Workspace.DisplayPreferences.LiveLowPassHz = 250;
            probe.SettingsStore.SavedSettings.Clear();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                AwaitOnDispatcher(probe.Workspace.StartPreparedPreviewAsync()));
            Assert.Contains("奈奎斯特", ex.Message);
            Assert.Null(probe.Workspace.ChannelMapping.ActiveConfiguration);
            Assert.Null(probe.Workspace.LiveDisplayMontageProfile);
            Assert.Equal(AcquisitionState.Ready, probe.Runtime.State.State);
            Assert.Empty(probe.SettingsStore.SavedSettings);
        });
    }

    [Fact]
    public void StartPreparedPreview_ValidatesConfiguresPersistsAndStartsInOrder()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            var montage = probe.CreateMontage("F88");
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = montage;
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.Workspace.DisplayPreferences.LiveLowPassHz = 70;
            probe.SettingsStore.SavedSettings.Clear();

            AwaitOnDispatcher(probe.Workspace.StartPreparedPreviewAsync());
            DrainDispatcher();

            Assert.Equal(AcquisitionState.Previewing, probe.Runtime.State.State);
            Assert.Same(montage, probe.Workspace.LiveDisplayMontageProfile);
            Assert.Equal("probe", probe.Workspace.ChannelMapping.ActiveConfigurationId);
            Assert.Single(probe.SettingsStore.SavedSettings);
            Assert.Equal(500, probe.SettingsStore.SavedSettings[0].SamplingRateHz);
            Assert.Equal("1", probe.SettingsStore.SavedSettings[0].ReferenceRangeText);
            Assert.Equal("2.5", probe.SettingsStore.SavedSettings[0].BipolarRangeText);
        });
    }

    [Fact]
    public void StartPreparedPreview_BlocksFilterMutationDuringPreparationWait()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            var montage = probe.CreateMontage("F88");
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = montage;
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.Workspace.DisplayPreferences.LiveLowPassHz = 70;

            var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var discoveryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.Driver.DiscoveryStarted = discoveryStarted;
            probe.Driver.DiscoveryGate = discoveryGate;

            var startTask = probe.Workspace.StartPreparedPreviewAsync();
            AwaitOnDispatcher(discoveryStarted.Task);

            try
            {
                Assert.False(probe.Workspace.DisplayPreferences.CanConfigureFilters);
                probe.Workspace.DisplayPreferences.LiveLowPassHz = 300;
                Assert.Equal(70, probe.Workspace.DisplayPreferences.LiveLowPassHz);
                probe.SettingsStore.SavedSettings.Clear();
            }
            finally { discoveryGate.SetResult(); }

            AwaitOnDispatcher(startTask);
            Assert.Equal(AcquisitionState.Previewing, probe.Runtime.State.State);
            Assert.True(probe.Workspace.DisplayPreferences.CanConfigureFilters);
            Assert.Equal(70, Assert.Single(probe.SettingsStore.SavedSettings).LiveLowPassHz);
        });
    }

    [Fact]
    public void StartPreparedPreview_BlocksFilterEditsWhileSavingAndUsesValidatedSettings()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = probe.CreateMontage("F88");
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.Workspace.DisplayPreferences.LiveLowPassHz = 70;
            probe.SettingsStore.SavedSettings.Clear();
            probe.SettingsStore.SavingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.SettingsStore.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var startTask = probe.Workspace.StartPreparedPreviewAsync();
            AwaitOnDispatcher(probe.SettingsStore.SavingStarted.Task);
            try
            {
                Assert.True(probe.Workspace.IsPreparationStartInProgress);
                Assert.False(probe.Workspace.DisplayPreferences.CanConfigureFilters);
                probe.Workspace.DisplayPreferences.LiveLowPassHz = 300;
                probe.Workspace.DisplayPreferences.LiveHighPassHz = 2;
                probe.Workspace.DisplayPreferences.LiveNotchHz = 60;
                Assert.Equal(70, probe.Workspace.DisplayPreferences.LiveLowPassHz);
                Assert.Equal(1, probe.Workspace.DisplayPreferences.LiveHighPassHz);
                Assert.Equal(0, probe.Workspace.DisplayPreferences.LiveNotchHz);
                AwaitOnDispatcher(Task.Delay(200));
            }
            finally { probe.SettingsStore.SaveGate.SetResult(); }
            AwaitOnDispatcher(startTask);

            Assert.Equal(AcquisitionState.Previewing, probe.Runtime.State.State);
            Assert.True(probe.Workspace.DisplayPreferences.CanConfigureFilters);
            Assert.Equal(70, Assert.Single(probe.SettingsStore.SavedSettings).LiveLowPassHz);
            Assert.Contains("1–70 Hz", probe.Workspace.OperationMessage);
            var bridge = typeof(ConfiguredAcquisitionRuntime).GetField("liveFilterBridge",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(probe.Runtime)!;
            var actualFilter = (LiveDisplayFilterSettings)bridge.GetType().GetField("activeSettings",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(bridge)!;
            Assert.Equal(new LiveDisplayFilterSettings(1, 70, null), actualFilter);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartPreparedPreview_SaveFailureOrChangedInputUnlocksAndAllowsRetry(bool changeInput)
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = probe.CreateMontage("F88");
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.SettingsStore.SavingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.SettingsStore.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var startTask = probe.Workspace.StartPreparedPreviewAsync();
            AwaitOnDispatcher(probe.SettingsStore.SavingStarted.Task);
            if (changeInput)
            {
                probe.Workspace.SelectedSamplingRateHz = 1000;
                probe.SettingsStore.SaveGate.SetResult();
            }
            else probe.SettingsStore.SaveGate.SetException(new IOException("Simulated save failure"));

            var error = Record.Exception(() => AwaitOnDispatcher(startTask));
            Assert.NotNull(error);
            Assert.Contains(changeInput ? "参数已修改" : "Simulated save failure", error.Message);
            Assert.NotEqual(AcquisitionState.Previewing, probe.Runtime.State.State);
            Assert.False(probe.Workspace.IsPreparationStartInProgress);
            Assert.True(probe.Workspace.DisplayPreferences.CanConfigureFilters);

            probe.SettingsStore.SaveGate = null;
            probe.Workspace.SelectedSamplingRateHz = 500;
            probe.Workspace.DisplayPreferences.LiveLowPassHz = 65;
            AwaitOnDispatcher(probe.Workspace.StartPreparedPreviewAsync());
            Assert.Equal(AcquisitionState.Previewing, probe.Runtime.State.State);
            Assert.Equal(65, probe.SettingsStore.SavedSettings[^1].LiveLowPassHz);
        });
    }

    [Fact]
    public void EventMarking_CapturesRecordingRelativeSampleCoordinatesAndRejectsInvalidState()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession();
            var montage = probe.CreateMontage("F88");
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = montage;
            probe.Workspace.SelectedSamplingRateHz = 500;

            var now = DateTimeOffset.UtcNow;
            var pointDef = new EventDefinition(
                Guid.NewGuid().ToString("N"), "EVT_BLINK", "Blink", string.Empty, "#2563EB", true, null,
                ShortcutScope.Acquisition, EventDefinitionSource.User, 1, now, now, EventKind.Point);
            var intervalDef = new EventDefinition(
                Guid.NewGuid().ToString("N"), "EVT_TASK", "Task", string.Empty, "#16A34A", true, null,
                ShortcutScope.Acquisition, EventDefinitionSource.User, 1, now, now, EventKind.Interval, "开始", "结束");
            AwaitOnDispatcher(probe.EventService.SaveAsync(pointDef, CancellationToken.None));
            AwaitOnDispatcher(probe.EventService.SaveAsync(intervalDef, CancellationToken.None));
            AwaitOnDispatcher(probe.Workspace.RefreshEventDefinitionsAsync());
            Assert.Contains(probe.Workspace.EnabledEventDefinitions, item => item.Id == pointDef.Id);
            Assert.Contains(probe.Workspace.EnabledEventDefinitions, item => item.Id == intervalDef.Id);

            // Must reject outside Recording state.
            Assert.Throws<InvalidOperationException>(() =>
                AwaitOnDispatcher(probe.Workspace.MarkEventAsync(pointDef.Id)));

            AwaitOnDispatcher(probe.Workspace.StartPreparedPreviewAsync());
            WaitForSamples(probe.Runtime);
            probe.Workspace.StartRecordingCommand.Execute(null);
            WaitForRecordingFirstSample(probe.Runtime);

            var marked = AwaitOnDispatcher(probe.Workspace.MarkEventAsync(pointDef.Id, EventSource.ManualButton, "test"));
            Assert.True(marked.StartSample >= 0);
            Assert.Equal(EventCoordinateStatus.Resolved, marked.CoordinateStatus);
            Assert.Contains(probe.Workspace.LiveRecordingEvents, item => item.Id == marked.Id);

            AwaitOnDispatcher(probe.Workspace.TriggerEventFromGridAsync(intervalDef));
            Assert.True(probe.Workspace.HasActiveInterval);
            Assert.Contains(probe.Workspace.LiveRecordingEvents,
                item => item.Id == AcquisitionWorkspaceViewModel.PendingIntervalEventId);

            Thread.Sleep(30);
            AwaitOnDispatcher(probe.Workspace.TriggerEventFromGridAsync(intervalDef));
            Assert.False(probe.Workspace.HasActiveInterval);
            Assert.DoesNotContain(probe.Workspace.LiveRecordingEvents,
                item => item.Id == AcquisitionWorkspaceViewModel.PendingIntervalEventId);
            Assert.Contains(probe.Workspace.LiveRecordingEvents,
                item => item.DefinitionId == intervalDef.Id && item.DurationSamples >= 1);
        });
    }

    [Fact]
    public void StopAndDispose_PreservesRecordingOnRegistrationFailureAndUnsubscribesCleanly()
    {
        RunOnDispatcher(() =>
        {
            using var probe = new ProbeSession(throwOnRegister: true);
            var montage = probe.CreateMontage("F88");
            probe.Workspace.SelectedProject = probe.ResearchProject;
            probe.Workspace.SelectedMontageProfile = montage;
            probe.Workspace.SelectedSamplingRateHz = 500;

            AwaitOnDispatcher(probe.Workspace.StartPreparedPreviewAsync());
            WaitForSamples(probe.Runtime);
            probe.Workspace.StartRecordingCommand.Execute(null);
            WaitForRecordingFirstSample(probe.Runtime);
            var recordingDir = probe.Runtime.RecordingDirectory!;

            var publishedMessages = new List<string>();
            probe.Notifications.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OperationNotificationCenter.Message))
                {
                    publishedMessages.Add(probe.Notifications.Message);
                }
            };

            // Registration failure must NOT fail FinishAcquisitionAsync.
            AwaitOnDispatcher(probe.Workspace.FinishAcquisitionAsync());
            DrainDispatcher();
            Assert.Equal(AcquisitionState.Stopped, probe.Runtime.State.State);
            Assert.True(File.Exists(Path.Combine(recordingDir, "manifest.json")));
            Assert.Null(probe.Workspace.LastRegisteredRecording);
            Assert.Contains(publishedMessages, message => message.Contains("原始记录已保存，但尚未注册到分析服务：Simulated backend offline"));

            // DisposeAsync must be idempotent and survive subsequent runtime/session updates.
            AwaitOnDispatcher(probe.Workspace.DisposeAsync().AsTask());
            AwaitOnDispatcher(probe.Workspace.DisposeAsync().AsTask());
            DrainDispatcher();
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

    private static void AwaitOnDispatcher(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static T AwaitOnDispatcher<T>(Task<T> task)
    {
        AwaitOnDispatcher((Task)task);
        return task.GetAwaiter().GetResult();
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

    private static void WaitForRecordingFirstSample(ConfiguredAcquisitionRuntime runtime)
    {
        var deadline = Environment.TickCount64 + 3000;
        while ((runtime.State.State != AcquisitionState.Recording || runtime.RecordingFirstSampleCounter is null) &&
               Environment.TickCount64 < deadline)
        {
            DrainDispatcher();
            Thread.Sleep(10);
        }
        Assert.Equal(AcquisitionState.Recording, runtime.State.State);
        Assert.NotNull(runtime.RecordingFirstSampleCounter);
    }

    private sealed class ProbeSession : IDisposable
    {
        private readonly string rootDir;
        private readonly HttpClient backendHttpClient;
        public ProbeDriver Driver { get; }
        public InMemorySettingsStore SettingsStore { get; }
        public ConfiguredAcquisitionRuntime Runtime { get; }
        public DeviceSessionManager Session { get; }
        public EventDefinitionService EventService { get; }
        public OperationNotificationCenter Notifications { get; } = new();
        public AcquisitionWorkspaceViewModel Workspace { get; }
        public ResearchProject ResearchProject { get; }

        public ProbeSession(
            double[]? referenceRanges = null,
            double[]? bipolarRanges = null,
            int? savedSamplingRateHz = null,
            string savedReferenceRangeText = "",
            string savedBipolarRangeText = "",
            bool throwOnRegister = false)
        {
            rootDir = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);
            backendHttpClient = new HttpClient(new DummyHttpHandler()) { BaseAddress = new Uri("http://localhost/") };
            Driver = new ProbeDriver(referenceRanges ?? [1.0], bipolarRanges ?? [2.5]);
            SettingsStore = new InMemorySettingsStore(savedSamplingRateHz, savedReferenceRangeText, savedBipolarRangeText, rootDir);
            Runtime = new ConfiguredAcquisitionRuntime(
                backendHttpClient: backendHttpClient,
                driverRegistry: new AcquisitionDriverRegistry([Driver]));
            Session = new DeviceSessionManager(Runtime);
            EventService = new EventDefinitionService(new EventDefinitionStore(Path.Combine(rootDir, "events.json")));
            var now = DateTimeOffset.UtcNow;
            ResearchProject = new ResearchProject("proj-1", "P001", "Synthetic Test", "", "", [], "tester", rootDir, now, now);
            Workspace = new AcquisitionWorkspaceViewModel(
                SettingsStore, Runtime, Session, Dispatcher.CurrentDispatcher,
                notifications: Notifications,
                eventDefinitionService: EventService,
                recordingRegistrationClient: throwOnRegister ? new FailingRegistrationClient() : null,
                montageStore: new MontageProfileStore(Path.Combine(rootDir, "montages.json")));
            Session.DiscoverAsync(
                new AcquisitionDriverConfiguration(AntEegoAcquisitionDriver.DriverId, new Dictionary<string, string>(),
                    Workspace.ChannelMapping.GetCurrentMapping()),
                CancellationToken.None).GetAwaiter().GetResult();
            DrainDispatcher();
        }

        public MontageProfile CreateMontage(string electrode)
        {
            var mapping = Workspace.ChannelMapping;
            Assert.True(DefaultChannelLabelTemplate.TryGetFor(Driver.Device.ChannelCapabilities!, out var labels));
            var entries = mapping.CaptureConfigurationEntries().Select(entry => entry with
            {
                ElectrodeLabel = entry.NativeChannelIndex switch
                {
                    3 => electrode,
                    5 => "F3",
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
            return new MontageProfile(
                "probe-montage", "Probe", "", MontageProfileSource.User,
                snapshot.DeviceSignature, fingerprint, snapshot, outputs,
                1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                MontageProfileFingerprint.Create(fingerprint, outputs));
        }

        public void Dispose()
        {
            Workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            DrainDispatcher();
            backendHttpClient.Dispose();
            if (Directory.Exists(rootDir)) Directory.Delete(rootDir, recursive: true);
        }
    }

    private sealed class DummyHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class InMemorySettingsStore(
        int? samplingRateHz, string referenceRangeText, string bipolarRangeText, string recordingDirectory) : IAcquisitionSettingsStore
    {
        public TaskCompletionSource? SavingStarted { get; set; }
        public TaskCompletionSource? SaveGate { get; set; }
        public List<AcquisitionConnectionSettings> SavedSettings { get; } = [];
        public AcquisitionConnectionSettings Load() => AcquisitionConnectionSettings.CreateDefault() with
        {
            SdkLibraryPath = "",
            RecordingDirectory = recordingDirectory,
            SamplingRateHz = samplingRateHz,
            ReferenceRangeText = referenceRangeText,
            BipolarRangeText = bipolarRangeText,
        };
        public Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken)
        {
            SavedSettings.Add(settings);
            SavingStarted?.TrySetResult();
            return SaveGate?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class FailingRegistrationClient : IRecordingRegistrationClient
    {
        public Task<RegisteredRecording> RegisterWpfRecordingAsync(
            string sourceDirectory, CancellationToken cancellationToken, IReadOnlyList<MontageProfile>? analysisMontageProfiles = null) =>
            throw new InvalidOperationException("Simulated backend offline");
    }

    private sealed class ProbeDriver(double[] referenceRanges, double[] bipolarRanges) : IAcquisitionDeviceDriver
    {
        public TaskCompletionSource? DiscoveryStarted { get; set; }
        public TaskCompletionSource? DiscoveryGate { get; set; }
        public AcquisitionDriverDescriptor Descriptor { get; } = new(
            AntEegoAcquisitionDriver.DriverId, "Synthetic ANT layout", "No SDK or hardware");
        public AcquisitionDeviceDescriptor Device { get; } = new(
            "probe:1", "Synthetic ANT layout", null, [500, 1000],
            ReferenceRangesVolts: referenceRanges,
            BipolarRangesVolts: bipolarRanges,
            ChannelCapabilities: Enumerable.Range(0, 30).Select(index =>
                new AcquisitionChannelCapability(index, index < 24 ? AcquisitionChannelKind.Reference
                    : index < 28 ? AcquisitionChannelKind.Bipolar
                    : index == 28 ? AcquisitionChannelKind.Trigger : AcquisitionChannelKind.SampleCounter,
                    index < 28 ? "V" : index == 28 ? "code" : "count")).ToArray());
        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) =>
            new ProbeAdapter(this, Device, configuration.ChannelLabelsByNativeIndex);
    }

    private sealed class ProbeAdapter(
        ProbeDriver driver,
        AcquisitionDeviceDescriptor device,
        IReadOnlyDictionary<int, string>? labels) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Synthetic only");
        public async Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
        {
            if (driver.DiscoveryGate is { } gate)
            {
                driver.DiscoveryStarted?.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
            }
            return [device];
        }
        public Task<IAcquisitionStream> OpenEegStreamAsync(AcquisitionStreamRequest request,
            CancellationToken cancellationToken) => Task.FromResult<IAcquisitionStream>(new ProbeStream(device, labels));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProbeStream(AcquisitionDeviceDescriptor device, IReadOnlyDictionary<int, string>? labels) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new("probe:1", "Synthetic ANT layout", 500,
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
                    _ => 10e-6,
                }).ToArray();
                yield return new AcquisitionBatch(first, 2, 30, values, DateTimeOffset.UtcNow);
                await Task.Delay(10, cancellationToken);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
