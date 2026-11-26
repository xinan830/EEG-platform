using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using BrainPlatform.Desktop.Infrastructure.Backend;
using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.UiTests;

internal static class ReviewStartupProbe
{
    public static void Run(bool closeDuringLoad = false)
    {
        var root = Directory.CreateTempSubdirectory("brain-review-startup-").FullName;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProductionApp.xaml")!;
        var xml = XDocument.Load(resource);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(xml.Descendants(presentation + "ResourceDictionary").First());
        dictionary.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        var merged = dictionary.Element(presentation + "ResourceDictionary.MergedDictionaries")!;
        foreach (var entry in merged.Elements())
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri(entry.Attribute("Source")!.Value, UriKind.Relative) });
        merged.Remove();
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(dictionary.ToString()));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        using var handler = new StartupHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture/") };
        var projects = new ResearchProjectStore(Path.Combine(root, "projects.json"));
        var montages = new MontageProfileStore(Path.Combine(root, "montages.json"), projects);
        var events = new EventDefinitionService(new EventDefinitionStore(Path.Combine(root, "events.json")));
        var acquisition = new AcquisitionWorkspaceViewModel(eventDefinitionService: events, montageStore: montages);
        var workspace = new DesktopWorkspaceViewModel(new HealthClient(), acquisition,
            eventDefinitionService: events, algorithmClient: new HttpAlgorithmClient(http), projectStore: projects,
            channelConfigurationStore: new ChannelConfigurationProfileStore(Path.Combine(root, "channels.json")),
            montageStore: montages, researchProjectClient: new HttpResearchProjectClient(http));
        MainWindow? owner = null;
        EegSessionWindow? review = null;
        try
        {
            workspace.Projects.SelectedProject = new ResearchProject("p", "P", "fixture", "", "", [],
                "fixture", root, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            owner = new MainWindow(null, new MainWindowViewSet()) { DataContext = workspace };
            _ = new WindowInteropHelper(owner).EnsureHandle();
            var row = new ProjectRecordingRow("s", "fixture.edf", "", "", "", "", "已完成", "",
                RecordingId: StartupHandler.RecordingId, SourceKind: "backend");
            var opening = owner.ShowRecordingReviewViewAsync(row, []);
            PumpUntil(() => handler.MetadataEntered.Task.IsCompleted);
            Assert.False(opening.IsCompleted);
            Assert.Null(GetField<EegSessionWindow>(owner, "reviewSessionWindow"));
            owner.ShowRecordingReviewViewAsync(row, []).GetAwaiter().GetResult();
            Assert.Equal(1, handler.MetadataReads);
            handler.MetadataRelease.TrySetResult();
            PumpUntil(() => handler.SamplesEntered.Task.IsCompleted || opening.IsCompleted);
            Assert.False(opening.IsCompleted, opening.Exception?.ToString());
            review = GetField<EegSessionWindow>(owner, "reviewSessionWindow");
            Assert.NotNull(review);
            Assert.True(review!.IsVisible);
            var model = GetField<RecordingReviewViewModel>(owner, "recordingReviewViewModel")!;
            Assert.True(model.IsLoading);
            Assert.Null(model.CurrentFrame);
            owner.ShowRecordingReviewViewAsync(row, []).GetAwaiter().GetResult();
            Assert.Equal(1, handler.MetadataReads);
            if (closeDuringLoad)
            {
                var closing = (Task)typeof(MainWindow).GetMethod("DisposeRecordingReviewAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
                PumpUntil(() => closing.IsCompleted);
                closing.GetAwaiter().GetResult();
                owner.DataContext = null;
                review.CloseCompletedSession();
            }
            handler.SamplesRelease.TrySetResult();
            PumpUntil(() => opening.IsCompleted);
            opening.GetAwaiter().GetResult();
            Assert.False(model.IsLoading);
            if (closeDuringLoad) Assert.Null(model.CurrentFrame);
            else
            {
                Assert.NotNull(model.CurrentFrame);
                Assert.Equal("F3-REF", model.CurrentFrame!.OutputChannelNames.Single());
                Assert.False(model.StatusText.Contains("正在"));
            }
        }
        finally
        {
            handler.MetadataRelease.TrySetResult();
            handler.SamplesRelease.TrySetResult();
            if (owner is not null)
            {
                var cleanup = (Task)typeof(MainWindow).GetMethod("DisposeRecordingReviewAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
                PumpUntil(() => cleanup.IsCompleted);
                cleanup.GetAwaiter().GetResult();
                owner.DataContext = null;
                review?.CloseCompletedSession();
            }
            var disposal = workspace.DisposeAsync().AsTask();
            PumpUntil(() => disposal.IsCompleted);
            disposal.GetAwaiter().GetResult();
            Directory.Delete(root, true);
            app.Shutdown();
        }
    }

    private static class Assert
    {
        public static void True(bool value, string? message = null)
        {
            if (!value) throw new InvalidOperationException(message ?? "Startup assertion failed.");
        }
        public static void False(bool value, string? message = null) => True(!value, message);
        public static void Null(object? value) => True(value is null);
        public static void NotNull(object? value) => True(value is not null);
        public static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual));
    }

    private static T? GetField<T>(MainWindow owner, string name) where T : class =>
        (T?)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Tick += (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(completed(), "Timed out waiting for review startup.");
    }

    private sealed class HealthClient : IBackendHealthClient
    {
        public Uri Endpoint => new("http://fixture/");
        public Task<BackendConnectionState> CheckAsync(CancellationToken token) =>
            Task.FromResult(BackendConnectionState.Checking(DateTimeOffset.Now));
    }

    private sealed class StartupHandler : HttpMessageHandler
    {
        public const string RecordingId = "11111111111111111111111111111111";
        public int MetadataReads;
        public TaskCompletionSource MetadataEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MetadataRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SamplesEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SamplesRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/projects/desktop/"))
                return Json(new BackendResearchProject("p", "fixture", "", "", ""));
            if (path == $"/api/recordings/{RecordingId}")
            {
                Interlocked.Increment(ref MetadataReads);
                MetadataEntered.TrySetResult();
                await MetadataRelease.Task.WaitAsync(token);
                var configuration = new ChannelConfigurationProfile("fixture-layout", "Fixture", "",
                    ChannelConfigurationSource.User, "fixture",
                    [new(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
                // Imported snapshots contain semantic mappings, without workstation save-time fingerprints.
                var montage = new
                {
                    Id = "fixture-ref", Name = "Fixture REF", DeviceSignature = "fixture",
                    ChannelConfigurationSnapshot = configuration,
                    DerivedChannels = new DerivedMontageChannel[]
                        { new("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0) },
                };
                return Json(new BackendRecording(RecordingId, "fixture.edf", ".edf", "2026-11-26T00:00:00Z",
                    250, 60, ["F3"], ["eeg"], ["V"], new string('a', 64), 1024,
                    ImportChannelConfigurationSnapshotJson: JsonSerializer.Serialize(configuration),
                    ImportChannelColumnMappingJson: "[{\"source_column\":0,\"native_channel_index\":0}]",
                    ImportAcquisitionMontageSnapshotJson: "{\"Id\":\"fixture-ref\"}",
                    ImportMontageConfigurationSnapshotJson: JsonSerializer.Serialize(montage)));
            }
            if (path.EndsWith("/raw-samples"))
            {
                SamplesEntered.TrySetResult();
                await SamplesRelease.Task.WaitAsync(token);
                var query = request.RequestUri.Query.TrimStart('?').Split('&')
                    .Select(item => item.Split('=')).ToDictionary(item => item[0], item => long.Parse(item[1]));
                var start = query["start_sample"];
                var end = query["end_sample"];
                return Json(new BackendRawSampleRange(RecordingId, start, end, 250, ["F3"], ["V"],
                    Enumerable.Range(0, checked((int)(end - start)))
                        .Select(_ => (IReadOnlyList<double?>)new double?[] { 1e-6 }).ToArray(), []));
            }
            if (path.EndsWith("/sessions") || path.EndsWith("/subjects")) return Json(Array.Empty<object>());
            throw new InvalidOperationException($"Unexpected startup request: {path}");
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
            { Content = JsonContent.Create(value) };
    }
}
