using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Filtering;
using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Creates stable display-source identities without reading signal values.</summary>
internal static class ReviewDisplaySourceIdentity
{
    public static string CreateRaw(LocalRawRecordingManifest manifest, MontageProfile? montage)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Hash(BuildCanonical(manifest, montage, "raw", null, null));
    }

    public static string CreateFiltered(
        LocalRawRecordingManifest manifest,
        MontageProfile? montage,
        RecordingReviewFilterSettings settings,
        ReviewFilterContract contract)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(contract);
        return Hash(BuildCanonical(manifest, montage, "filtered", settings, contract));
    }

    private static string BuildCanonical(
        LocalRawRecordingManifest manifest,
        MontageProfile? montage,
        string sourceKind,
        RecordingReviewFilterSettings? settings,
        ReviewFilterContract? contract)
    {
        var builder = new StringBuilder()
            .Append("review-display-source-v2|")
            .Append(manifest.SessionId.ToString("N")).Append('|')
            .Append(sourceKind).Append('|')
            .Append(manifest.PayloadFormat.Trim()).Append('|')
            .Append(manifest.EegSignalUnit.Trim()).Append('|')
            .Append(manifest.DeviceId.Trim()).Append('|')
            .Append(manifest.DeviceName.Trim()).Append('|')
            .Append(manifest.SamplingRateHz).Append('|')
            .Append(montage is null ? "montage:none" : MontageProfileFingerprint.Create(
                montage.ChannelConfigurationFingerprint, montage.DerivedChannels,
                montage.AverageReferenceLabels));

        foreach (var channel in manifest.Channels.OrderBy(channel => channel.StreamIndex))
        {
            builder.Append('|')
                .Append(channel.StreamIndex).Append(':')
                .Append(channel.NativeChannelIndex).Append(':')
                .Append(channel.Label?.Trim().ToUpperInvariant()).Append(':')
                .Append(channel.Kind).Append(':')
                .Append(channel.Unit?.Trim().ToUpperInvariant());
        }

        if (settings is { } filterSettings && contract is { } filterContract)
        {
            builder.Append("|filter:")
                .Append(filterSettings.HighPassHz.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(filterSettings.LowPassHz.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(filterSettings.NotchHz?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "off").Append(':')
                .Append(filterContract.AlgorithmVersion).Append(':')
                .Append(filterContract.Fingerprint).Append(':')
                .Append(filterContract.CheckpointVersion);
        }

        return builder.ToString();
    }

    private static string Hash(string canonical) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
