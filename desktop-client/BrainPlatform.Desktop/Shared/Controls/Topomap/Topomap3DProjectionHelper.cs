using System.Windows;
using System.Windows.Media.Media3D;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

internal readonly record struct ProjectedElectrodeLabel(
    Point ScreenPoint,
    double SphereScreenRadiusPx,
    bool IsVisible,
    double Opacity);

/// <summary>
/// Right-handed WPF Viewport3D projection and shortest-path target orientation math
/// for 3D electrode labels and smooth focus animations.
/// </summary>
internal static class Topomap3DProjectionHelper
{
    internal static (double TargetYawDegrees, double TargetPitchDegrees) ComputeFocusAngles(
        Point3D localPosition,
        double currentYawDegrees)
    {
        var rawYaw = Math.Atan2(localPosition.X, -localPosition.Z) * 180d / Math.PI;
        var targetYaw = currentYawDegrees + NormalizeShortestAngleDelta(rawYaw - currentYawDegrees);
        var horizontalRadius = Math.Sqrt(localPosition.X * localPosition.X + localPosition.Z * localPosition.Z);
        var rawPitch = -Math.Atan2(localPosition.Y, horizontalRadius) * 180d / Math.PI;
        var targetPitch = Math.Clamp(rawPitch, -75d, 75d);
        return (targetYaw, targetPitch);
    }

    internal static double NormalizeShortestAngleDelta(double deltaDegrees)
    {
        var wrapped = (deltaDegrees + 180d) % 360d;
        if (wrapped < 0) wrapped += 360d;
        return wrapped - 180d;
    }

    internal static double EaseInOutCubic(double t)
    {
        var clamped = Math.Clamp(t, 0d, 1d);
        return clamped < 0.5
            ? 4d * clamped * clamped * clamped
            : 1d - Math.Pow(-2d * clamped + 2d, 3d) / 2d;
    }

    internal static Transform3DGroup BuildRotationTransform(double yawDegrees, double pitchDegrees)
    {
        var rotation = new Transform3DGroup();
        rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yawDegrees)));
        rotation.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitchDegrees)));
        return rotation;
    }

    internal static ProjectedElectrodeLabel ProjectLabel(
        Topomap3DElectrodeMarker marker,
        Transform3D sceneTransform,
        PerspectiveCamera camera,
        double viewportWidth,
        double viewportHeight,
        bool isSelected)
    {
        if (viewportWidth <= 1 || viewportHeight <= 1 || camera.LookDirection.LengthSquared <= 1e-12)
        {
            return new ProjectedElectrodeLabel(default, 0, false, 0);
        }

        var markerWorldPos = sceneTransform.Transform(marker.Position);
        var worldNormal = sceneTransform.Transform(marker.OutwardNormal);
        if (worldNormal.LengthSquared > 1e-12) worldNormal.Normalize();

        var toCamera = camera.Position - markerWorldPos;
        if (toCamera.LengthSquared <= 1e-12) return new ProjectedElectrodeLabel(default, 0, false, 0);
        toCamera.Normalize();

        var facingDot = Vector3D.DotProduct(worldNormal, toCamera);
        if (!isSelected && facingDot <= 0.06)
        {
            return new ProjectedElectrodeLabel(default, 0, false, 0);
        }

        var forward = camera.LookDirection;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, camera.UpDirection);
        if (right.LengthSquared <= 1e-12) return new ProjectedElectrodeLabel(default, 0, false, 0);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);
        up.Normalize();

        var offset = markerWorldPos - camera.Position;
        var depth = Vector3D.DotProduct(offset, forward);
        if (depth <= Math.Max(0.001, camera.NearPlaneDistance * 0.5))
        {
            return new ProjectedElectrodeLabel(default, 0, false, 0);
        }

        // WPF PerspectiveCamera.FieldOfView is the horizontal field of view in degrees.
        var halfWidth = depth * Math.Tan(camera.FieldOfView * Math.PI / 360d);
        var halfHeight = halfWidth * (viewportHeight / viewportWidth);
        if (halfWidth <= 1e-9 || halfHeight <= 1e-9)
        {
            return new ProjectedElectrodeLabel(default, 0, false, 0);
        }

        var ndcX = Vector3D.DotProduct(offset, right) / halfWidth;
        var ndcY = Vector3D.DotProduct(offset, up) / halfHeight;
        if (ndcX < -1.15 || ndcX > 1.15 || ndcY < -1.15 || ndcY > 1.15)
        {
            return new ProjectedElectrodeLabel(default, 0, false, 0);
        }

        var screenX = (ndcX + 1d) * 0.5 * viewportWidth;
        var screenY = (1d - ndcY) * 0.5 * viewportHeight;
        var effectiveRadius3D = marker.MarkerRadius * (isSelected ? 1.85 : 1.05);
        var sphereScreenRadiusPx = (effectiveRadius3D / halfHeight) * (viewportHeight * 0.5);
        var opacity = isSelected
            ? (facingDot > -0.15 ? 1d : 0.45)
            : Math.Clamp((facingDot - 0.06) / 0.22, 0d, 1d);
        return new ProjectedElectrodeLabel(new Point(screenX, screenY), sphereScreenRadiusPx, opacity > 0.02, opacity);
    }
}
