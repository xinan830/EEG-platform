using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public partial class Topomap3DView : UserControl
{
    public static readonly DependencyProperty PresentationProperty = DependencyProperty.Register(
        nameof(Presentation), typeof(Topomap3DPresentation), typeof(Topomap3DView),
        new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty ScalpModelProperty = DependencyProperty.Register(
        nameof(ScalpModel), typeof(ElectrodeScalpModel3D), typeof(Topomap3DView),
        new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty ElectrodeLabelsProperty = DependencyProperty.Register(
        nameof(ElectrodeLabels), typeof(IReadOnlyList<string>), typeof(Topomap3DView),
        new PropertyMetadata(Array.Empty<string>(), OnPresentationChanged));
    public static readonly DependencyProperty BandIdProperty = DependencyProperty.Register(
        nameof(BandId), typeof(string), typeof(Topomap3DView),
        new PropertyMetadata("alpha", OnPresentationChanged));
    public static readonly DependencyProperty ColorScaleStateProperty = DependencyProperty.Register(
        nameof(ColorScaleState), typeof(Topomap3DColorScaleState), typeof(Topomap3DView),
        new PropertyMetadata(null, OnPresentationChanged));

    private CancellationTokenSource? preparation;
    private Point? dragOrigin;
    private double yaw;
    private double pitch;
    private bool cameraWasAdjusted;
    private string? framedModelIdentity;
    private const double InitialFieldOfViewDegrees = 42;
    private const double MinimumFieldOfViewDegrees = 2;

    public Topomap3DView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Topomap3DPresentation? Presentation
    {
        get => (Topomap3DPresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
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

    public string BandId
    {
        get => (string)GetValue(BandIdProperty);
        set => SetValue(BandIdProperty, value);
    }

    public Topomap3DColorScaleState? ColorScaleState
    {
        get => (Topomap3DColorScaleState?)GetValue(ColorScaleStateProperty);
        set => SetValue(ColorScaleStateProperty, value);
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Topomap3DView)sender).SchedulePreparation();

    private void OnLoaded(object sender, RoutedEventArgs e) => SchedulePreparation();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelPreparation();
    }

    private void SchedulePreparation()
    {
        CancelPreparation();
        if (!IsLoaded || string.IsNullOrWhiteSpace(BandId)) return;
        var presentation = Presentation;
        var model = presentation?.Model ?? ScalpModel;
        if (model is null) return;
        var labels = ElectrodeLabels.ToArray();
        var cancellation = preparation = new CancellationTokenSource();
        var token = cancellation.Token;
        var band = presentation?.Result.Bands.FirstOrDefault(item => item.BandId == BandId && item.Status == "ready");
        if (presentation is not null && band is null)
        {
            UnavailableText.Text = "当前频段暂无有效数据";
            UnavailableText.Visibility = Visibility.Visible;
            return;
        }
        if (band is not null) ColorScaleState?.Remember(band.BandId, band.Unit, band.ValueMin, band.ValueMax);
        var bounds = band is null ? null : ColorScaleState?.GetBounds(band.BandId, band.Unit);
        UnavailableText.Text = SceneRoot.Children.Count == 0 ? "准备三维地形图" : "三维地形图更新中";
        UnavailableText.Visibility = Visibility.Visible;
        _ = PrepareAsync(presentation, model, band, labels, bounds, cancellation, token);
    }

    private async Task PrepareAsync(Topomap3DPresentation? presentation, ElectrodeScalpModel3D model,
        Topomap3DBand? band, IReadOnlyList<string> labels,
        (double Minimum, double Maximum)? bounds, CancellationTokenSource owner, CancellationToken token)
    {
        try
        {
            var scene = await Task.Run(() => presentation is not null && band is not null
                    ? Topomap3DScenePreparer.Prepare(presentation, band.BandId, token, bounds)
                    : Topomap3DScenePreparer.PrepareElectrodeLocations(model, labels, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(preparation, owner)) return;
            SceneRoot.Children.Clear();
            SceneRoot.Children.Add(scene);
            if (presentation is not null && band is not null)
            {
                BandText.Text = $"{BandLabel(band.BandId)}  {presentation.Result.PowerType}";
                WindowText.Text = $"{presentation.Result.WindowStartSeconds:0.###}–{presentation.Result.WindowEndSeconds:0.###} s";
                MinimumText.Text = (bounds?.Minimum ?? band.ValueMin ?? 0).ToString("0.###");
                MaximumText.Text = (bounds?.Maximum ?? band.ValueMax ?? 1).ToString("0.###");
                UnitText.Text = band.Unit;
                ColorLegend.Visibility = Visibility.Visible;
                UnavailableText.Visibility = Visibility.Collapsed;
            }
            else
            {
                BandText.Text = "标准电极位置";
                WindowText.Text = "只显示已启用且有标准 3D 坐标的 EEG 电极";
                ColorLegend.Visibility = Visibility.Collapsed;
                var hasMappedLabel = HasMappedElectrode(model, labels);
                UnavailableText.Text = labels.Count == 0
                    ? "当前配置没有启用的 EEG 通道"
                    : hasMappedLabel ? string.Empty : "当前启用通道没有可用的标准 3D 位置";
                UnavailableText.Visibility = string.IsNullOrEmpty(UnavailableText.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            var modelIdentity = $"{model.ModelId}|{model.ModelVersion}|{model.MeshGeometrySha256}";
            if (!string.Equals(framedModelIdentity, modelIdentity, StringComparison.Ordinal))
            {
                framedModelIdentity = modelIdentity;
                FrameCamera(scene.Bounds);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(preparation, owner)) return;
            DesktopLog.Error("review_topomap_3d_scene_failed", exception);
            UnavailableText.Text = "三维地形图资源或结果无效";
            UnavailableText.Visibility = Visibility.Visible;
        }
    }

    private static string BandLabel(string band) => band switch
    {
        "delta" => "Delta 波", "theta" => "Theta 波", "alpha" => "Alpha 波",
        "beta" => "Beta 波", "gamma" => "Gamma 波", _ => band,
    };

    private static bool HasMappedElectrode(ElectrodeScalpModel3D model, IReadOnlyList<string> labels)
    {
        var selected = labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return model.ElectrodeLocations.Any(site =>
            selected.Contains(site.Channel) || selected.Contains(site.CanonicalChannel));
    }

    private void FrameCamera(Rect3D bounds)
    {
        if (SurfaceViewport.ActualWidth <= 0 || SurfaceViewport.ActualHeight <= 0) return;
        var frame = Topomap3DScenePreparer.CalculateCameraFrame(
            bounds, SurfaceViewport.ActualWidth, SurfaceViewport.ActualHeight, Camera.FieldOfView);
        Camera.Position = frame.Position;
        Camera.LookDirection = frame.LookDirection;
        Camera.NearPlaneDistance = frame.NearPlane;
        Camera.FarPlaneDistance = frame.FarPlane;
        cameraWasAdjusted = false;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!cameraWasAdjusted && SceneRoot.Children.Count > 0) FrameCamera(SceneRoot.Bounds);
    }

    private void CancelPreparation()
    {
        preparation?.Cancel();
        preparation?.Dispose();
        preparation = null;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        dragOrigin = e.GetPosition(this);
        SurfaceViewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (dragOrigin is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        yaw += (current.X - origin.X) * 0.6;
        pitch = Math.Clamp(pitch + (current.Y - origin.Y) * 0.6, -80, 80);
        cameraWasAdjusted = true;
        dragOrigin = current;
        var rotation = new Transform3DGroup();
        rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yaw)));
        rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitch)));
        SceneRoot.Transform = rotation;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        dragOrigin = null;
        if (SurfaceViewport.IsMouseCaptured) SurfaceViewport.ReleaseMouseCapture();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Camera.FieldOfView = CalculateZoomFieldOfView(Camera.FieldOfView, e.Delta);
        cameraWasAdjusted = true;
        e.Handled = true;
    }

    internal static double CalculateZoomFieldOfView(double currentFieldOfViewDegrees, int wheelDelta) =>
        Math.Clamp(currentFieldOfViewDegrees * Math.Pow(1.12, -wheelDelta / 120d),
            MinimumFieldOfViewDegrees, InitialFieldOfViewDegrees);

}
