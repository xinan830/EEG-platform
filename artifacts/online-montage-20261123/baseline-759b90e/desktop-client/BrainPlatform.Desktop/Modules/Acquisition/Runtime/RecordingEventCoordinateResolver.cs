namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

/// <summary>
/// Converts a device counter into the immutable recording time axis without
/// compressing sample-counter gaps or treating a reset as continuous time.
/// </summary>
public static class RecordingEventCoordinateResolver
{
    public static RecordingEventCoordinate Resolve(
        long sourceSampleCounter,
        long recordingFirstSampleCounter,
        IReadOnlyList<AcquisitionGap> recordingGaps)
    {
        ArgumentNullException.ThrowIfNull(recordingGaps);
        if (sourceSampleCounter < recordingFirstSampleCounter)
        {
            return new RecordingEventCoordinate(
                0,
                sourceSampleCounter,
                EventCoordinateStatus.UnavailableDiscontinuity,
                "sample_counter_precedes_recording_origin");
        }

        var relativeSample = checked(sourceSampleCounter - recordingFirstSampleCounter);
        var gap = recordingGaps.FirstOrDefault(item =>
            sourceSampleCounter >= item.FirstMissingSampleCounter &&
            sourceSampleCounter <= item.LastMissingSampleCounter);
        return gap is null
            ? new RecordingEventCoordinate(relativeSample, sourceSampleCounter)
            : new RecordingEventCoordinate(
                relativeSample,
                sourceSampleCounter,
                EventCoordinateStatus.UnavailableGap,
                "sample_counter_gap");
    }
}
