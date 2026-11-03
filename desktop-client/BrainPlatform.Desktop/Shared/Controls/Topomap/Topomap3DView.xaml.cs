using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private CancellationTokenSource? preparation;
    private Point? dragOrigin;
    private bool isPanning;
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

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Topomap3DView)sender).SchedulePreparation();

    private void OnLoaded(object sender, RoutedEventArgs e) => SchedulePreparation();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelPreparation();
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
        UnavailableText.Text = SceneRoot.Children.Count == 0 ? "准备头模" : "头模更新中";
        UnavailableText.Visibility = Visibility.Visible;
        _ = PrepareAsync(model, labels, cancellation, token);
    }

    private async Task PrepareAsync(ElectrodeScalpModel3D model, IReadOnlyList<string> labels,
        CancellationTokenSource owner, CancellationToken token)
    {
        try
        {
            var scene = await Task.Run(() =>
                Topomap3DScenePreparer.PrepareElectrodeLocations(model, labels, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(preparation, owner)) return;
            SceneRoot.Children.Clear();
            SceneRoot.Children.Add(scene);
            BandText.Text = "标准电极位置";
            WindowText.Text = "只显示已启用且有标准 3D 坐标的 EEG 电极";
            var hasMappedLabel = HasMappedElectrode(model, labels);
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
                FrameCamera(scene.Bounds);
            }
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
        if (!cameraWasAdjusted && SceneRoot.Children.Count > 0)
            FrameCamera(SceneRoot.Bounds);
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
            var rotation = new Transform3DGroup();
            rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yaw)));
            rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitch)));
            SceneRoot.Transform = rotation;
        }
        cameraWasAdjusted = true;
        dragOrigin = current;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        dragOrigin = null;
        isPanning = false;
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

    internal static Vector3D CalculatePanOffset(double deltaX, double deltaY, double viewportHeight,
        double fieldOfViewDegrees, Vector3D lookDirection, Vector3D upDirection)
    {
        if (viewportHeight <= 0 || lookDirection.LengthSquared <= 0) return default;
        var look = lookDirection;
        look.Normalize();
        var right = Vector3D.CrossProduct(upDirection, look);
        if (right.LengthSquared <= 0) return default;
        right.Normalize();
        var cameraUp = Vector3D.CrossProduct(look, right);
        cameraUp.Normalize();
        var unitsPerPixel = 2 * lookDirection.Length * Math.Tan(fieldOfViewDegrees * Math.PI / 360) / viewportHeight;
        return -right * (deltaX * unitsPerPixel) + cameraUp * (deltaY * unitsPerPixel);
    }

}
