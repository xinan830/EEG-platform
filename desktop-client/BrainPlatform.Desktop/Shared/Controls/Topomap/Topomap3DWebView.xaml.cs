using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Api;
using Microsoft.Web.WebView2.Core;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

/// <summary>
/// WebGL2 / Three.js 3D brain visualization host using WebView2 with automatic fallback to
/// native WPF <see cref="Topomap3DView"/> if the WebView2 runtime or assets are unavailable.
/// </summary>
public partial class Topomap3DWebView : UserControl
{
    private const string VirtualHostName = "topomap3d.local";
    private const string ViewerUrl = $"https://{VirtualHostName}/viewer.html";

    private static readonly object EnvironmentGate = new();
    private static Task<CoreWebView2Environment>? sharedEnvironmentTask;

    public static readonly DependencyProperty ScalpModelProperty = DependencyProperty.Register(
        nameof(ScalpModel), typeof(ElectrodeScalpModel3D), typeof(Topomap3DWebView),
        new PropertyMetadata(null, OnSceneInputChanged));

    public static readonly DependencyProperty ElectrodeLabelsProperty = DependencyProperty.Register(
        nameof(ElectrodeLabels), typeof(IReadOnlyList<string>), typeof(Topomap3DWebView),
        new PropertyMetadata(Array.Empty<string>(), OnSceneInputChanged));

    public static readonly DependencyProperty SelectedElectrodeLabelProperty = DependencyProperty.Register(
        nameof(SelectedElectrodeLabel), typeof(string), typeof(Topomap3DWebView),
        new PropertyMetadata(null, OnSelectedElectrodeChanged));

    public static readonly DependencyProperty IsHeatmapPreviewProperty = DependencyProperty.Register(
        nameof(IsHeatmapPreview), typeof(bool), typeof(Topomap3DWebView),
        new PropertyMetadata(false, OnSceneInputChanged));

    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(TopomapField), typeof(Topomap3DWebView),
        new PropertyMetadata(null, OnSceneInputChanged));

    public static readonly DependencyProperty ColorScaleStateProperty = DependencyProperty.Register(
        nameof(ColorScaleState), typeof(TopomapColorScaleState), typeof(Topomap3DWebView),
        new PropertyMetadata(null, OnSceneInputChanged));

    public static readonly DependencyProperty ShowDimensionSwitcherProperty = DependencyProperty.Register(
        nameof(ShowDimensionSwitcher), typeof(bool), typeof(Topomap3DWebView),
        new PropertyMetadata(false, OnSceneInputChanged));

    public static readonly DependencyProperty WindowTextProperty = DependencyProperty.Register(
        nameof(WindowText), typeof(string), typeof(Topomap3DWebView),
        new PropertyMetadata(null, OnSceneInputChanged));

    public static readonly DependencyProperty EnablePreviewLabModesProperty = DependencyProperty.Register(
        nameof(EnablePreviewLabModes), typeof(bool), typeof(Topomap3DWebView),
        new PropertyMetadata(false, OnSceneInputChanged));

    private CancellationTokenSource? syncCts;
    private bool initStarted;
    private bool viewerReady;
    private bool usingFallback;
    private Topomap3DView? fallbackView;
    private string? sentScalpIdentity;
    private bool cortexSent;

    public event EventHandler? SwitchTo2DRequested;
    public event EventHandler? FallbackActivated;

    public bool IsUsingFallback => usingFallback;

    public Topomap3DWebView()
    {
        InitializeComponent();
        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 248, 250, 252);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ElectrodeScalpModel3D? ScalpModel
    {
        get => (ElectrodeScalpModel3D?)GetValue(ScalpModelProperty);
        set => SetValue(ScalpModelProperty, value);
    }

    public IReadOnlyList<string> ElectrodeLabels
    {
        get => (IReadOnlyList<string>)GetValue(ElectrodeLabelsProperty);
        set => SetValue(ElectrodeLabelsProperty, value);
    }

    public string? SelectedElectrodeLabel
    {
        get => (string?)GetValue(SelectedElectrodeLabelProperty);
        set => SetValue(SelectedElectrodeLabelProperty, value);
    }

    public bool IsHeatmapPreview
    {
        get => (bool)GetValue(IsHeatmapPreviewProperty);
        set => SetValue(IsHeatmapPreviewProperty, value);
    }

    public TopomapField? Field
    {
        get => (TopomapField?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public TopomapColorScaleState? ColorScaleState
    {
        get => (TopomapColorScaleState?)GetValue(ColorScaleStateProperty);
        set => SetValue(ColorScaleStateProperty, value);
    }

    public bool ShowDimensionSwitcher
    {
        get => (bool)GetValue(ShowDimensionSwitcherProperty);
        set => SetValue(ShowDimensionSwitcherProperty, value);
    }

    public string? WindowText
    {
        get => (string?)GetValue(WindowTextProperty);
        set => SetValue(WindowTextProperty, value);
    }

    public bool EnablePreviewLabModes
    {
        get => (bool)GetValue(EnablePreviewLabModesProperty);
        set => SetValue(EnablePreviewLabModesProperty, value);
    }

    private static void OnSceneInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Topomap3DWebView)d).ScheduleSync();

    private static void OnSelectedElectrodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (Topomap3DWebView)d;
        if (view.usingFallback && view.fallbackView is not null)
        {
            view.fallbackView.SelectedElectrodeLabel = view.SelectedElectrodeLabel;
            return;
        }
        view.PostSelectedElectrode();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (usingFallback)
        {
            SyncFallbackProperties();
            return;
        }

        if (!initStarted)
        {
            initStarted = true;
            _ = InitializeWebViewAsync();
        }
        else if (viewerReady)
        {
            ScheduleSync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelSync();
    }

    private static Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
    {
        lock (EnvironmentGate)
        {
            if (sharedEnvironmentTask is null || sharedEnvironmentTask.IsFaulted || sharedEnvironmentTask.IsCanceled)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BrainPlatform", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                sharedEnvironmentTask = CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }
            return sharedEnvironmentTask;
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var assetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Topomap3D");
            if (!File.Exists(Path.Combine(assetsFolder, "viewer.html")) ||
                !File.Exists(Path.Combine(assetsFolder, "vendor", "three.module.min.js")))
            {
                throw new FileNotFoundException("离线 Three.js 3D 渲染资源缺失。", assetsFolder);
            }

            var environment = await GetSharedEnvironmentAsync();
            await WebView.EnsureCoreWebView2Async(environment);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                VirtualHostName, assetsFolder, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnWebMessageReceived;
            core.Navigate(ViewerUrl);
        }
        catch (Exception ex)
        {
            DesktopLog.Error("topomap_3d_webview_init_failed", ex);
            ActivateFallback();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl)) return;
            var msgType = typeEl.GetString();
            if (string.Equals(msgType, "ready", StringComparison.Ordinal))
            {
                viewerReady = true;
                StatusText.Visibility = Visibility.Collapsed;
                ScheduleSync();
            }
            else if (string.Equals(msgType, "switchTo2D", StringComparison.Ordinal))
            {
                SwitchTo2DRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Warning($"topomap_3d_webview_message_error={ex.Message}");
        }
    }

    private void ActivateFallback()
    {
        usingFallback = true;
        WebView.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        fallbackView ??= new Topomap3DView();
        FallbackHost.Content = fallbackView;
        FallbackHost.Visibility = Visibility.Visible;
        SyncFallbackProperties();
        FallbackActivated?.Invoke(this, EventArgs.Empty);
    }

    private void SyncFallbackProperties()
    {
        if (fallbackView is null) return;
        fallbackView.ScalpModel = ScalpModel;
        fallbackView.ElectrodeLabels = ElectrodeLabels;
        fallbackView.SelectedElectrodeLabel = SelectedElectrodeLabel;
        fallbackView.IsHeatmapPreview = IsHeatmapPreview;
        fallbackView.ColorScaleState = ColorScaleState;
        fallbackView.Field = Field;
    }

    private void ScheduleSync()
    {
        if (usingFallback)
        {
            SyncFallbackProperties();
            return;
        }
        if (!IsLoaded || !viewerReady || WebView.CoreWebView2 is null) return;

        CancelSync();
        var cts = new CancellationTokenSource();
        syncCts = cts;
        var scalp = ScalpModel;
        var labels = ElectrodeLabels ?? Array.Empty<string>();
        var field = Field;
        var scaleState = ColorScaleState;
        var isPreview = IsHeatmapPreview;
        var showDim = ShowDimensionSwitcher;
        var windowText = WindowText;
        var enableLabModes = EnablePreviewLabModes;
        _ = SyncSceneAsync(scalp, labels, field, scaleState, isPreview, showDim, windowText, enableLabModes, cts, cts.Token);
    }

    private async Task SyncSceneAsync(
        ElectrodeScalpModel3D? scalp,
        IReadOnlyList<string> labels,
        TopomapField? field,
        TopomapColorScaleState? scaleState,
        bool isPreview,
        bool showDim,
        string? windowText,
        bool enableLabModes,
        CancellationTokenSource owner,
        CancellationToken token)
    {
        try
        {
            scalp ??= await Topomap3DModelProvider.GetScalpModelAsync(token);
            if (token.IsCancellationRequested || !ReferenceEquals(syncCts, owner) || WebView.CoreWebView2 is null) return;

            var identity = $"{scalp.ModelId}|{scalp.ModelVersion}|{scalp.MeshGeometrySha256}";
            if (!string.Equals(sentScalpIdentity, identity, StringComparison.Ordinal))
            {
                var scalpMessage = JsonSerializer.Serialize(new { type = "setScalpModel", model = scalp });
                WebView.CoreWebView2.PostWebMessageAsJson(scalpMessage);
                sentScalpIdentity = identity;
            }

            if (!cortexSent)
            {
                try
                {
                    var cortexJson = await Topomap3DModelProvider.GetCortexModelJsonAsync(token);
                    if (token.IsCancellationRequested || !ReferenceEquals(syncCts, owner) || WebView.CoreWebView2 is null) return;
                    WebView.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"setCortexModel\",\"model\":{cortexJson}}}");
                    cortexSent = true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DesktopLog.Warning($"topomap_3d_cortex_unavailable={ex.Message}");
                }
            }

            var sceneMessage = Topomap3DWebPayloadBuilder.BuildSceneStateJson(
                scalp, labels, field, scaleState, isPreview, showDim, windowText, enableLabModes);
            WebView.CoreWebView2.PostWebMessageAsJson(sceneMessage);
            PostSelectedElectrode();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(syncCts, owner)) return;
            DesktopLog.Error("topomap_3d_webview_sync_failed", ex);
        }
    }

    private void PostSelectedElectrode()
    {
        if (!viewerReady || usingFallback || WebView.CoreWebView2 is null) return;
        var msg = JsonSerializer.Serialize(
            new { type = "selectElectrode", label = SelectedElectrodeLabel },
            Topomap3DWebPayloadBuilder.CamelJsonOptions);
        WebView.CoreWebView2.PostWebMessageAsJson(msg);
    }

    private void CancelSync()
    {
        syncCts?.Cancel();
        syncCts?.Dispose();
        syncCts = null;
    }
}
