using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

public partial class Topomap3DView : UserControl
{
    public static readonly DependencyProperty ScalpModelProperty = DependencyProperty.Register(
        nameof(ScalpModel), typeof(ElectrodeScalpModel3D), typeof(Topomap3DView),
        new PropertyMetadata(null, OnModelChanged));
    public static readonly DependencyProperty ElectrodeLabelsProperty = DependencyProperty.Register(
        nameof(ElectrodeLabels), typeof(IReadOnlyList<string>), typeof(Topomap3DView),
        new PropertyMetadata(Array.Empty<string>(), OnModelChanged));
    public static readonly DependencyProperty SelectedElectrodeLabelProperty = DependencyProperty.Register(
        nameof(SelectedElectrodeLabel), typeof(string), typeof(Topomap3DView),
        new PropertyMetadata(null, OnSelectedElectrodeChanged));

    private const double InitialFieldOfViewDegrees = 42;
    private const double MinimumFieldOfViewDegrees = 2;
    private const double FocusDurationSeconds = 0.56;
    private const double ResetDurationSeconds = 0.45;
    private const double HighlightPulseDurationSeconds = 1.2;

    private CancellationTokenSource? preparation;
    private IReadOnlyList<Topomap3DElectrodeMarker> currentMarkers = [];
    private readonly List<(Topomap3DElectrodeMarker Marker, Border Pill, TextBlock Text)> labelVisuals = [];
    private Point? dragOrigin;
    private bool isPanning;
    private double yaw;
    private double pitch;
    private bool cameraWasAdjusted;
    private string? framedModelIdentity;
    private Point3D defaultCameraPosition = new(0, 0.18, -1.7);
    private Vector3D defaultCameraLookDirection = new(0, -0.08, 1);

    // Smooth camera focus / reset animation state
    private bool isAnimatingCamera;
    private double animStartSeconds;
    private double animDurationSeconds;
    private double animStartYaw, animTargetYaw;
    private double animStartPitch, animTargetPitch;
    private Point3D animStartPos, animTargetPos;
    private double animStartFov, animTargetFov;

    // 3-second selected electrode highlight pulse state
    private bool isPulsingHighlight;
    private double pulseStartSeconds;
    private ScaleTransform3D? selectedMarkerScale;
    private SolidColorBrush? selectedMarkerEmissiveBrush;
    private bool renderLoopAttached;
    private readonly Stopwatch clock = Stopwatch.StartNew();

    public Topomap3DView()
    {
        InitializeComponent();
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

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Topomap3DView)sender).SchedulePreparation();

    private static void OnSelectedElectrodeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Topomap3DView)sender).ApplySelectedElectrode(animateToTarget: true);

    private void OnLoaded(object sender, RoutedEventArgs e) => SchedulePreparation();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelPreparation();
        StopAllAnimations();
    }

    private void SchedulePreparation()
    {
        CancelPreparation();
        if (!IsLoaded) return;
        var model = ScalpModel;
        if (model is null) return;
        var labels = ElectrodeLabels.ToArray();
        var cancellation = preparation = new CancellationTokenSource();
        var token = cancellation.Token;
        UnavailableText.Text = StaticSceneRoot.Children.Count == 0 ? "准备头模" : "头模更新中";
        UnavailableText.Visibility = Visibility.Visible;
        _ = PrepareAsync(model, labels, cancellation, token);
    }

    private async Task PrepareAsync(ElectrodeScalpModel3D model, IReadOnlyList<string> labels,
        CancellationTokenSource owner, CancellationToken token)
    {
        try
        {
            var sceneData = await Task.Run(() =>
                Topomap3DScenePreparer.PrepareElectrodeSceneData(model, labels, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(preparation, owner)) return;
            StaticSceneRoot.Children.Clear();
            StaticSceneRoot.Children.Add(sceneData.Scene);
            currentMarkers = sceneData.Markers;
            RebuildLabelVisuals();

            BandText.Text = "标准电极位置";
            WindowText.Text = "只显示已启用且有标准 3D 坐标的 EEG 电极";
            var hasMappedLabel = currentMarkers.Count > 0;
            UnavailableText.Text = labels.Count == 0
                ? "当前配置没有启用的 EEG 通道"
                : hasMappedLabel ? string.Empty : "当前启用通道没有可用的标准 3D 位置";
            UnavailableText.Visibility = string.IsNullOrEmpty(UnavailableText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var modelIdentity = $"{model.ModelId}|{model.ModelVersion}|{model.MeshGeometrySha256}";
            if (!string.Equals(framedModelIdentity, modelIdentity, StringComparison.Ordinal))
            {
                framedModelIdentity = modelIdentity;
                FrameCamera(StaticSceneRoot.Bounds);
            }

            ApplySelectedElectrode(animateToTarget: !string.IsNullOrWhiteSpace(SelectedElectrodeLabel));
            UpdateProjectedLabels();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(preparation, owner)) return;
            DesktopLog.Error("electrode_scalp_3d_scene_failed", exception);
            UnavailableText.Text = "三维头模资源无效";
            UnavailableText.Visibility = Visibility.Visible;
        }
    }

    private void RebuildLabelVisuals()
    {
        ElectrodeLabelOverlay.Children.Clear();
        labelVisuals.Clear();
        var halo = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.White,
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.9,
        };
        halo.Freeze();

        foreach (var marker in currentMarkers)
        {
            var text = new TextBlock
            {
                Text = marker.DisplayLabel,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Effect = halo,
            };
            var pill = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Child = text,
                Visibility = Visibility.Collapsed,
            };
            ElectrodeLabelOverlay.Children.Add(pill);
            labelVisuals.Add((marker, pill, text));
        }
    }

    private void ApplySelectedElectrode(bool animateToTarget)
    {
        SelectedMarkerRoot.Children.Clear();
        selectedMarkerScale = null;
        selectedMarkerEmissiveBrush = null;
        isPulsingHighlight = false;

        var selectedLabel = SelectedElectrodeLabel?.Trim();
        var match = string.IsNullOrWhiteSpace(selectedLabel)
            ? null
            : currentMarkers.FirstOrDefault(marker =>
                string.Equals(marker.DisplayLabel, selectedLabel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker.CanonicalChannel, selectedLabel, StringComparison.OrdinalIgnoreCase));

        UpdateLabelHighlightStyles(match);
        if (match is null)
        {
            UpdateProjectedLabels();
            return;
        }

        // Independent glowing highlight sphere centered on the selected electrode (no scalp rebuild).
        var mesh = Topomap3DScenePreparer.CreateSingleSphereMesh(match.Position, match.MarkerRadius * 1.42);
        var diffuseBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22));
        selectedMarkerEmissiveBrush = new SolidColorBrush(Color.FromRgb(255, 140, 40));
        var materials = new MaterialGroup();
        materials.Children.Add(new DiffuseMaterial(diffuseBrush));
        materials.Children.Add(new EmissiveMaterial(selectedMarkerEmissiveBrush));
        materials.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 48));

        selectedMarkerScale = new ScaleTransform3D(1, 1, 1, match.Position.X, match.Position.Y, match.Position.Z);
        var model = new GeometryModel3D(mesh, materials)
        {
            BackMaterial = materials,
            Transform = selectedMarkerScale,
        };
        SelectedMarkerRoot.Children.Add(model);

        isPulsingHighlight = true;
        pulseStartSeconds = clock.Elapsed.TotalSeconds;

        if (animateToTarget && IsLoaded && StaticSceneRoot.Children.Count > 0)
        {
            var (targetYaw, targetPitch) = Topomap3DProjectionHelper.ComputeFocusAngles(match.Position, yaw);
            StartCameraAnimation(targetYaw, targetPitch, defaultCameraPosition, Camera.FieldOfView, FocusDurationSeconds);
        }

        EnsureRenderLoopAttached();
        UpdateProjectedLabels();
    }

    private void UpdateLabelHighlightStyles(Topomap3DElectrodeMarker? selectedMarker)
    {
        foreach (var (marker, pill, text) in labelVisuals)
        {
            var isSelected = selectedMarker is not null &&
                string.Equals(marker.CanonicalChannel, selectedMarker.CanonicalChannel, StringComparison.OrdinalIgnoreCase);
            if (isSelected)
            {
                text.Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12));
                text.FontSize = 13;
                text.FontWeight = FontWeights.Bold;
                Panel.SetZIndex(pill, 100);
            }
            else
            {
                text.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                text.FontSize = 11.5;
                text.FontWeight = FontWeights.SemiBold;
                Panel.SetZIndex(pill, 1);
            }
        }
    }

    private void StartCameraAnimation(
        double targetYaw, double targetPitch, Point3D targetPos, double targetFov, double durationSeconds)
    {
        isAnimatingCamera = true;
        animStartSeconds = clock.Elapsed.TotalSeconds;
        animDurationSeconds = Math.Max(0.05, durationSeconds);
        animStartYaw = yaw;
        animTargetYaw = targetYaw;
        animStartPitch = pitch;
        animTargetPitch = targetPitch;
        animStartPos = Camera.Position;
        animTargetPos = targetPos;
        animStartFov = Camera.FieldOfView;
        animTargetFov = targetFov;
        EnsureRenderLoopAttached();
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e)
    {
        if (StaticSceneRoot.Children.Count == 0) return;
        Camera.LookDirection = defaultCameraLookDirection;
        var targetYaw = yaw + Topomap3DProjectionHelper.NormalizeShortestAngleDelta(-yaw);
        StartCameraAnimation(targetYaw, 0, defaultCameraPosition, InitialFieldOfViewDegrees, ResetDurationSeconds);
        cameraWasAdjusted = false;
    }

    private void EnsureRenderLoopAttached()
    {
        if (renderLoopAttached) return;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
        renderLoopAttached = true;
    }

    private void DetachRenderLoopIfIdle()
    {
        if (!renderLoopAttached || isAnimatingCamera || isPulsingHighlight) return;
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        renderLoopAttached = false;
    }

    private void StopAllAnimations()
    {
        isAnimatingCamera = false;
        isPulsingHighlight = false;
        if (renderLoopAttached)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            renderLoopAttached = false;
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        var labelsNeedUpdate = false;

        if (isAnimatingCamera)
        {
            var rawT = (now - animStartSeconds) / animDurationSeconds;
            var eased = Topomap3DProjectionHelper.EaseInOutCubic(rawT);
            yaw = animStartYaw + (animTargetYaw - animStartYaw) * eased;
            pitch = animStartPitch + (animTargetPitch - animStartPitch) * eased;
            Camera.Position = new Point3D(
                animStartPos.X + (animTargetPos.X - animStartPos.X) * eased,
                animStartPos.Y + (animTargetPos.Y - animStartPos.Y) * eased,
                animStartPos.Z + (animTargetPos.Z - animStartPos.Z) * eased);
            Camera.FieldOfView = animStartFov + (animTargetFov - animStartFov) * eased;
            ApplySceneRotation();
            labelsNeedUpdate = true;

            if (rawT >= 1d)
            {
                yaw = Topomap3DProjectionHelper.NormalizeShortestAngleDelta(animTargetYaw);
                pitch = animTargetPitch;
                Camera.Position = animTargetPos;
                Camera.FieldOfView = animTargetFov;
                ApplySceneRotation();
                isAnimatingCamera = false;
            }
        }

        if (isPulsingHighlight && selectedMarkerScale is not null && selectedMarkerEmissiveBrush is not null)
        {
            var elapsed = now - pulseStartSeconds;
            if (elapsed >= HighlightPulseDurationSeconds)
            {
                selectedMarkerScale.ScaleX = 1;
                selectedMarkerScale.ScaleY = 1;
                selectedMarkerScale.ScaleZ = 1;
                selectedMarkerEmissiveBrush.Color = Color.FromRgb(180, 75, 16);
                isPulsingHighlight = false;
            }
            else
            {
                // Exactly 2 crisp pulses over 1.2 seconds (0 -> peak -> base -> peak -> base).
                var progress = elapsed / HighlightPulseDurationSeconds;
                var wave = 0.5 + 0.5 * Math.Sin(progress * Math.PI * 4.0 - Math.PI / 2d);
                var scale = 1.0 + 0.32 * wave;
                selectedMarkerScale.ScaleX = scale;
                selectedMarkerScale.ScaleY = scale;
                selectedMarkerScale.ScaleZ = scale;
                var r = (byte)(170 + 85 * wave);
                var g = (byte)(65 + 115 * wave);
                var b = (byte)(15 + 65 * wave);
                selectedMarkerEmissiveBrush.Color = Color.FromRgb(r, g, b);
            }
        }

        if (labelsNeedUpdate) UpdateProjectedLabels();
        DetachRenderLoopIfIdle();
    }

    private void ApplySceneRotation() =>
        SceneRoot.Transform = Topomap3DProjectionHelper.BuildRotationTransform(yaw, pitch);

    private void UpdateProjectedLabels()
    {
        if (labelVisuals.Count == 0 || SurfaceViewport.ActualWidth <= 1 || SurfaceViewport.ActualHeight <= 1) return;
        var transform = SceneRoot.Transform ?? Transform3D.Identity;
        var selectedLabel = SelectedElectrodeLabel?.Trim();
        foreach (var (marker, pill, _) in labelVisuals)
        {
            var isSelected = !string.IsNullOrWhiteSpace(selectedLabel) &&
                (string.Equals(marker.DisplayLabel, selectedLabel, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(marker.CanonicalChannel, selectedLabel, StringComparison.OrdinalIgnoreCase));
            var projected = Topomap3DProjectionHelper.ProjectLabel(
                marker, transform, Camera, SurfaceViewport.ActualWidth, SurfaceViewport.ActualHeight, isSelected);
            if (!projected.IsVisible)
            {
                pill.Visibility = Visibility.Collapsed;
                continue;
            }

            pill.Visibility = Visibility.Visible;
            pill.Opacity = projected.Opacity;
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var gapPx = projected.SphereScreenRadiusPx + 5d;
            Canvas.SetLeft(pill, projected.ScreenPoint.X - pill.DesiredSize.Width * 0.5);
            Canvas.SetTop(pill, projected.ScreenPoint.Y - gapPx - pill.DesiredSize.Height);
        }
    }

    private void FrameCamera(Rect3D bounds)
    {
        if (SurfaceViewport.ActualWidth <= 0 || SurfaceViewport.ActualHeight <= 0) return;
        var frame = Topomap3DScenePreparer.CalculateCameraFrame(
            bounds, SurfaceViewport.ActualWidth, SurfaceViewport.ActualHeight, InitialFieldOfViewDegrees);
        defaultCameraPosition = frame.Position;
        defaultCameraLookDirection = frame.LookDirection;
        Camera.Position = frame.Position;
        Camera.LookDirection = frame.LookDirection;
        Camera.FieldOfView = InitialFieldOfViewDegrees;
        Camera.NearPlaneDistance = frame.NearPlane;
        Camera.FarPlaneDistance = frame.FarPlane;
        cameraWasAdjusted = false;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!cameraWasAdjusted && StaticSceneRoot.Children.Count > 0)
            FrameCamera(StaticSceneRoot.Bounds);
        UpdateProjectedLabels();
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
        isAnimatingCamera = false;
        dragOrigin = e.GetPosition(this);
        isPanning = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        SurfaceViewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (dragOrigin is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        if (isPanning)
        {
            Camera.Position += CalculatePanOffset(deltaX, deltaY, SurfaceViewport.ActualHeight,
                Camera.FieldOfView, Camera.LookDirection, Camera.UpDirection);
        }
        else
        {
            yaw += deltaX * 0.6;
            pitch = Math.Clamp(pitch + deltaY * 0.6, -80, 80);
            ApplySceneRotation();
        }
        cameraWasAdjusted = true;
        dragOrigin = current;
        UpdateProjectedLabels();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        dragOrigin = null;
        isPanning = false;
        if (SurfaceViewport.IsMouseCaptured) SurfaceViewport.ReleaseMouseCapture();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        isAnimatingCamera = false;
        Camera.FieldOfView = CalculateZoomFieldOfView(Camera.FieldOfView, e.Delta);
        cameraWasAdjusted = true;
        UpdateProjectedLabels();
        e.Handled = true;
    }

    internal static double CalculateZoomFieldOfView(double currentFieldOfViewDegrees, int wheelDelta) =>
        Math.Clamp(currentFieldOfViewDegrees * Math.Pow(1.12, -wheelDelta / 120d),
            MinimumFieldOfViewDegrees, InitialFieldOfViewDegrees);

    internal static Vector3D CalculatePanOffset(double deltaX, double deltaY, double viewportHeight,
        double fieldOfViewDegrees, Vector3D lookDirection, Vector3D upDirection)
    {
        if (viewportHeight <= 0 || lookDirection.LengthSquared <= 0) return default;
        var look = lookDirection;
        look.Normalize();
        var right = Vector3D.CrossProduct(look, upDirection);
        if (right.LengthSquared <= 0) return default;
        right.Normalize();
        var cameraUp = Vector3D.CrossProduct(right, look);
        cameraUp.Normalize();
        var unitsPerPixel = 2 * lookDirection.Length * Math.Tan(fieldOfViewDegrees * Math.PI / 360) / viewportHeight;
        return -right * (deltaX * unitsPerPixel) + cameraUp * (deltaY * unitsPerPixel);
    }
}
