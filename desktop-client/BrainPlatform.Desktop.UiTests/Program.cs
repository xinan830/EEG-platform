using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using BrainPlatform.Desktop.Infrastructure.Backend;

namespace BrainPlatform.Desktop.UiTests;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        try
        {
            // Load the production resources without running App.OnStartup or starting hardware/backend services.
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProductionApp.xaml")!;
            var xml = XDocument.Load(resource);
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var dictionary = new XElement(xml.Descendants(presentation + "ResourceDictionary").First());
            dictionary.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            var fixture = new AcquisitionUiFixture(root);
            var window = new Window
            {
                Title = "BrainPlatform - Synthetic acquisition UI regression",
                Width = 1440, Height = 1000, DataContext = fixture.Workspace,
                Content = new AcquisitionPreparationView(),
            };
            app.MainWindow = window;
            var evidence = new AcquisitionUiEvidence(fixture, window);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => evidence.Capture();
            window.Loaded += async (_, _) =>
            {
                try { await fixture.InitializeAsync(); timer.Start(); evidence.Capture(); }
                catch (Exception exception) { File.WriteAllText(Path.Combine(root, "failure.txt"), exception.ToString()); }
            };
            fixture.Runtime.StateChanged += (_, state) => window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (state.State == AcquisitionState.Previewing && window.Content is AcquisitionPreparationView)
                    window.Content = new AcquisitionWorkspaceView();
            }));
            var closing = false;
            window.Closing += async (_, args) =>
            {
                if (closing) return;
                args.Cancel = true;
                closing = true;
                timer.Stop();
                try { evidence.Capture(); await fixture.Workspace.DisposeAsync(); }
                finally { window.Close(); }
            };
            app.DispatcherUnhandledException += (_, args) =>
            {
                File.AppendAllText(Path.Combine(root, "failure.txt"), args.Exception + Environment.NewLine);
                args.Handled = true;
            };
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "last-run.txt"), root);
            app.Run(window);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(root, "failure.txt"), exception.ToString());
            throw;
        }
    }
}

internal sealed class AcquisitionUiFixture
{
    public string Root { get; }
    public ConfiguredAcquisitionRuntime Runtime { get; }
    public DesktopWorkspaceViewModel Workspace { get; }
    private readonly SyntheticDriver driver = new();
    private readonly ChannelConfigurationProfileStore channels;
    private readonly MontageProfileStore montages;

    public AcquisitionUiFixture(string root)
    {
        Root = root;
        channels = new ChannelConfigurationProfileStore(Path.Combine(root, "channel-profiles.json"));
        montages = new MontageProfileStore(Path.Combine(root, "montage-profiles.json"));
        var http = new HttpClient(new IdentityFilterHandler()) { BaseAddress = new Uri("http://synthetic.invalid/") };
        Runtime = new ConfiguredAcquisitionRuntime(http, new AcquisitionDriverRegistry([driver]));
        var session = new DeviceSessionManager(Runtime);
        var events = new EventDefinitionService(new EventDefinitionStore(Path.Combine(root, "event-definitions.json")));
        var acquisition = new AcquisitionWorkspaceViewModel(new MemorySettings(root), Runtime, session, eventDefinitionService: events);
        Workspace = new DesktopWorkspaceViewModel(new BackendHealthClient(http), acquisition,
            eventDefinitionService: events, algorithmClient: new HttpAlgorithmClient(http),
            projectStore: new ResearchProjectStore(Path.Combine(root, "projects.json")),
            channelConfigurationStore: channels, montageStore: montages);
    }

    public async Task InitializeAsync()
    {
        var acquisition = Workspace.Acquisition;
        acquisition.SdkLibraryPath = "synthetic-sdk-not-loaded";
        await acquisition.DeviceSession.DiscoverAsync(AntEegoAcquisitionDriver.CreateConfiguration(
            new AntEegoAdapterOptions(acquisition.SdkLibraryPath, null, null)), CancellationToken.None);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        acquisition.SelectedProject = new ResearchProject("ui-test", "UI-TEST", "Synthetic regression only",
            "No real EEG", "UI test", [], "test", Root, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Workspace.Projects.SelectedProject = acquisition.SelectedProject;
        foreach (var montage in new[] { CreateMontage(false), CreateMontage(true) })
        {
            await channels.SaveAsync(montage.ChannelConfigurationSnapshot, CancellationToken.None);
            await montages.SaveAsync(montage, CancellationToken.None);
        }
        await Workspace.MontageConfigurations.RefreshAsync();
        // Deliberately unselected: the automation must use the real ComboBox.
        acquisition.SelectedMontageProfile = null;
    }

    private MontageProfile CreateMontage(bool custom)
    {
        var mapping = Workspace.Acquisition.ChannelMapping;
        if (!DefaultChannelLabelTemplate.TryGetFor(driver.Device.ChannelCapabilities!, out var labels))
            throw new InvalidOperationException("Synthetic template missing.");
        var entries = mapping.CaptureConfigurationEntries().Select(entry => entry with
        {
            ElectrodeLabel = custom && entry.NativeChannelIndex == 3 ? "F88"
                : custom && entry.NativeChannelIndex == 5 ? "F3" : labels.GetValueOrDefault(entry.NativeChannelIndex, ""),
            IsSelectedForDisplay = entry.NativeChannelIndex is 0 or 1 or 3 or 5,
        }).ToArray();
        var snapshot = new ChannelConfigurationProfile(custom ? "875d753fe1fc49189fe98d1d169f604a" : "4cbe57d823c34bd6890b88f6aacfd5ae", custom ? "CH3=F88, CH5=F3" : "Default mapping",
            "Synthetic fixture", ChannelConfigurationSource.User, mapping.GetDeviceSignature(), entries, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        ChannelConfigurationValidation.Validate(snapshot);
        var outputs = entries.Where(entry => entry.IsSelectedForDisplay).Select(entry => new DerivedMontageChannel(
            $"{entry.ElectrodeLabel}-REF", entry.ElectrodeLabel, MontageNegativeKind.OriginalHardwareReference, [], entry.DisplayOrder)).ToArray();
        var fingerprint = ChannelConfigurationFingerprint.Create(snapshot);
        return new MontageProfile(custom ? "ui-custom" : "ui-default", custom ? "Synthetic F88 mapping" : "Synthetic default mapping", "",
            MontageProfileSource.User, snapshot.DeviceSignature, fingerprint, snapshot, outputs, 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MontageProfileFingerprint.Create(fingerprint, outputs));
    }
}
