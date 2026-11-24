using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class ElectrodeMapLandmarkDraftTests
{
    [Fact]
    public void UpdatingOneLandmarkLeavesTheOtherUnchanged_AndResetRestoresBothDefaults()
    {
        var draft = new ElectrodeMapLandmarkDraft();
        var movedReference = new ElectrodeMapPosition(-0.2, 0.4);

        draft.ReferencePosition = movedReference;

        Assert.Equal(movedReference, draft.ReferencePosition);
        Assert.Equal(ElectrodeMapDefaults.Ground, draft.GroundPosition);

        draft.Reset();

        Assert.Equal(ElectrodeMapDefaults.Reference, draft.ReferencePosition);
        Assert.Equal(ElectrodeMapDefaults.Ground, draft.GroundPosition);
    }

    [Fact]
    public void PositionOutsideHeadDiagramIsRejected()
    {
        var draft = new ElectrodeMapLandmarkDraft();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            draft.GroundPosition = new ElectrodeMapPosition(0.8, 0.8));
    }
}
