using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Montages;

namespace BrainPlatform.Desktop.Tests.Review;

internal static class ReviewMontageSelectionFixture
{
    public static WorkspaceMontageSelection<MontageProfile> Create(
        IRecordingAccessSession reader,
        RecordingMontageCatalogResult catalog) => new(
        reader.Manifest.SessionId.ToString("N"),
        catalog.AcquisitionMontage,
        catalog.CompatibleViewingMontages.Select(item => item.Profile));
}
