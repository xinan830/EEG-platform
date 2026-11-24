namespace BrainPlatform.Desktop.Modules.Review.Waveform;

public sealed partial class RecordingReviewSciChartCanvas
{
    private void UpdateTimePresentation(RecordingReviewViewModel? viewModel)
    {
        recordingTimeLabels.RecordingStartUtc = viewModel?.RecordingStartUtc;
        recordingTimeLabels.Context = viewModel?.TimeDisplay;
        // Both modes share recording-second coordinates; only their labels change.
        wallClockTicks.OriginUtc = DateTimeOffset.UnixEpoch;
        xAxis.InvalidateElement();
    }
}
