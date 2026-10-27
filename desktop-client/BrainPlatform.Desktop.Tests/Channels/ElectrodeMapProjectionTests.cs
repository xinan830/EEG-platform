using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.Modules.Channels.Views;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ElectrodeMapProjectionTests
{
    [Fact]
    public void ProjectsMriXyWithAnteriorUpAndPatientLeftOnViewerLeft()
    {
        var rows = new[]
        {
            Row("Fp1", -0.03, 0.08),
            Row("Fp2", 0.03, 0.08),
        };

        var points = ElectrodeMapProjection.Project(rows);

        Assert.Equal(-0.225, points[0].X, 6);
        Assert.Equal(-0.705, points[0].Y, 6);
        Assert.True(points[0].X < 0);
        Assert.True(points[1].X > 0);
        Assert.True(points[0].Y < 0);
    }

    [Fact]
    public void OmitsNonEegAndUntrustedOrContradictoryLocations()
    {
        var rows = new[]
        {
            Row("Cz", 0, 0.02),
            Row("REF", 0, 0.01, isEeg: false),
            Row("GND", 0, 0.01, isEeg: false),
            Row("Fp1", 0.02, 0.08, source: "mne.other"),
            Row("F3", 0.02, 0.08, isEeg: false),
            Row("O1", 0, 0.01, frame: "unknown"),
        };

        Assert.Equal(["Cz"], ElectrodeMapProjection.Project(rows).Select(point => point.Label));
    }

    [Fact]
    public void ProjectsManualDiagramPositionForCustomElectrodeLabel()
    {
        var row = new ChannelConfigurationDraftRow(1, "Reference", "V", "µV", "MyRef", true, 0, true);
        row.SetManualMapPosition(new ElectrodeMapPosition(0.25, -0.4));

        Assert.Equal(new ElectrodeMapPoint("MyRef", 0.25, -0.4), ElectrodeMapProjection.Project([row]).Single());
    }

    [Fact]
    public void ManualDiagramPositionOverridesProjectionWithoutChangingStandardCoordinates()
    {
        var row = Row("F3", -0.03, 0.04);
        var standardLocation = row.ElectrodeLocation;
        row.SetManualMapPosition(new ElectrodeMapPosition(0.45, -0.2));

        Assert.Equal(new ElectrodeMapPoint("F3", 0.45, -0.2), ElectrodeMapProjection.Project([row]).Single());
        Assert.Same(standardLocation, row.ElectrodeLocation);
    }

    [Fact]
    public void ResetToTemplatePositionRestoresStandardProjection()
    {
        var row = Row("F3", -0.03, 0.04);
        var standardPoint = ElectrodeMapProjection.Project([row]).Single();
        row.SetManualMapPosition(new ElectrodeMapPosition(0.45, -0.2));

        row.ResetToTemplatePosition();

        Assert.Equal(standardPoint, ElectrodeMapProjection.Project([row]).Single());
        Assert.Null(row.ManualMapPosition);
    }

    [Fact]
    public void ResetToTemplatePositionRemovesCustomElectrodeWithoutTemplate()
    {
        var row = new ChannelConfigurationDraftRow(1, "EEG", "V", "µV", "Custom", true, 0, true,
            manualMapPosition: new ElectrodeMapPosition(0.25, -0.4));

        row.ResetToTemplatePosition();

        Assert.Null(row.ManualMapPosition);
        Assert.Empty(ElectrodeMapProjection.Project([row]));
    }

    [Fact]
    public void ResettingAllRowsRestoresEveryStandardPositionAndRemovesCustomPosition()
    {
        var standard = Row("F3", -0.03, 0.04);
        var custom = new ChannelConfigurationDraftRow(1, "EEG", "V", "µV", "Custom", true, 1, true,
            manualMapPosition: new ElectrodeMapPosition(0.25, -0.4));
        standard.SetManualMapPosition(new ElectrodeMapPosition(0.45, -0.2));
        var rows = new[] { standard, custom };

        foreach (var row in rows)
        {
            row.ResetToTemplatePosition();
        }

        var point = ElectrodeMapProjection.Project(rows).Single();
        Assert.Equal("F3", point.Label);
        Assert.Equal(-0.225, point.X, 6);
        Assert.Equal(-0.405, point.Y, 6);
        Assert.All(rows, row => Assert.Null(row.ManualMapPosition));
    }

    [Fact]
    public void ClearingCustomElectrodePositionRemovesItFromProjection()
    {
        var row = new ChannelConfigurationDraftRow(1, "EEG", "V", "µV", "Custom", true, 0, true);
        row.SetManualMapPosition(new ElectrodeMapPosition(0.25, -0.4));

        row.SetManualMapPosition(null);

        Assert.Empty(ElectrodeMapProjection.Project([row]));
    }

    [Fact]
    public void StandardPositionedEegChannelCanBeManuallyPlaced()
    {
        var row = Row("F3", -0.03, 0.04);

        Assert.True(ElectrodePositionMap.CanPlaceRow(row));
        Assert.False(ElectrodePositionMap.CanPlaceRow(Row("REF", 0, 0, isEeg: false)));
        Assert.False(ElectrodePositionMap.CanPlaceRow(null));
    }

    [Fact]
    public void OmitsPositionWhenChannelIsNotSelectedForDisplay()
    {
        var row = Row("Fp1", -0.03, 0.08);
        row.IsEnabled = false;

        Assert.Empty(ElectrodeMapProjection.Project([row]));
    }

    [Fact]
    public void ProjectsHardwareReferenceAndGroundAsIndependentDefaultLandmarks()
    {
        var points = ElectrodeMapProjection.ProjectWithHardwareLandmarks([]);

        Assert.Equal(new ElectrodeMapPoint("REF", 0.0036, 0.4514, true), points[0]);
        Assert.Equal(new ElectrodeMapPoint("GND", 0.0021, -0.7338, true), points[1]);
        Assert.All(points, point => Assert.True(point.IsHardwareLandmark));
    }

    [Fact]
    public void AllowsProfileSpecificHardwareLandmarkPositionsWithoutEEGRows()
    {
        var points = ElectrodeMapProjection.ProjectWithHardwareLandmarks([], new ElectrodeMapPosition(-0.1, 0.3), new ElectrodeMapPosition(0.1, -0.8));

        Assert.Equal(new ElectrodeMapPoint("REF", -0.1, 0.3, true), points[0]);
        Assert.Equal(new ElectrodeMapPoint("GND", 0.1, -0.8, true), points[1]);
    }

    [Theory]
    [InlineData(0.0036, 0.4514, "REF")]
    [InlineData(0.0021, -0.7338, "GND")]
    public void HitTestFindsHardwareLandmarksIndependentlyOfSelectedChannel(double x, double y, string expected)
    {
        const double width = 400;
        const double height = 400;
        var centerX = width / 2;
        var centerY = height / 2 + 5;
        var radius = Math.Min(width, height - 24) * 0.39;
        var pointer = new System.Windows.Point(centerX + x * radius, centerY + y * radius);

        var result = ElectrodePositionMap.HitTestHardwareLandmark(
            pointer, width, height, ElectrodeMapDefaults.Reference, ElectrodeMapDefaults.Ground, 12);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void OmitsManualPositionWhenElectrodeLabelIsBlank()
    {
        var row = new ChannelConfigurationDraftRow(1, "EEG", "V", "µV", "", true, 0, true);
        row.SetManualMapPosition(new ElectrodeMapPosition(0.25, -0.4));

        Assert.Empty(ElectrodeMapProjection.Project([row]));
    }

    private static ChannelConfigurationDraftRow Row(
        string label,
        double x,
        double y,
        bool isEeg = true,
        string frame = "mri",
        string source = "mne.standard_1020") => new(
            0, isEeg ? "EEG" : "REF", "V", "µV", label, true, 0, isEeg,
            new ElectrodeLocationSnapshot(label, x, y, 0, frame, "m", source, "1.12.1"));
}
