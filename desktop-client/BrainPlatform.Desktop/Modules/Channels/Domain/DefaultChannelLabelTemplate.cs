using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Modules.Channels.Domain;

/// <summary>
/// The local default physical-input layout. It is data only: no EEG file is
/// embedded, loaded, or copied into the application package.
/// </summary>
public static class DefaultChannelLabelTemplate
{
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [0] = "Fp1", [1] = "Fp2", [2] = "F7", [3] = "F3", [4] = "Fz", [5] = "F4", [6] = "F8",
        [7] = "T3", [8] = "C3", [9] = "Cz", [10] = "C4", [11] = "T4", [12] = "M1", [13] = "T5",
        [14] = "P3", [15] = "Pz", [16] = "P4", [17] = "T6", [18] = "M2", [19] = "O1", [21] = "O2",
    };

    public static bool TryGetFor(
        IReadOnlyList<AcquisitionChannelCapability> capabilities,
        out IReadOnlyDictionary<int, string> labels)
    {
        var eegChannels = capabilities
            .Where(capability => capability.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .ToArray();
        var matchesExpectedLayout = eegChannels.Length == 28 &&
            Enumerable.Range(0, 24).All(index => eegChannels.Any(channel =>
                channel.NativeChannelIndex == index && channel.Kind == AcquisitionChannelKind.Reference)) &&
            Enumerable.Range(24, 4).All(index => eegChannels.Any(channel =>
                channel.NativeChannelIndex == index && channel.Kind == AcquisitionChannelKind.Bipolar));
        if (!matchesExpectedLayout)
        {
            labels = new Dictionary<int, string>();
            return false;
        }

        labels = Labels;
        return true;
    }
}
