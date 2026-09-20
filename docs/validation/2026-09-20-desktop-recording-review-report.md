# Desktop recording review validation

Date: 2026-09-20

## Automated evidence

- `dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false` — **152 passed, 0 failed**.
- `dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --no-restore -p:UseAppHost=false` — **0 warnings, 0 errors**.
- `openspec validate add-desktop-recording-review --strict --no-interactive` — **valid**.
- `git diff --check` — **passed** before this report was added.
- Focused performance test `RecordingReviewPerformanceTests.FourKilohertzThirtyColumnWindowIsBoundedToRequestedSamples` — **1 passed**. It indexes a 4 kHz / 30-column one-second chunk without payload reads during open and reads only a 0.5-second window.

## Covered behavior

- Project recordings expose a `回溯` action only for readable completed/aborted recordings.
- Review opens without global navigation and returns to the owning project list.
- Raw sample-major float64/V chunks are indexed by headers and read by requested counter window.
- Sample-counter gaps remain separate render segments; no zero fill or interpolation is introduced.
- Acquisition montage is loaded from the immutable recording snapshot. Legacy recordings without a snapshot open on an explicit raw-signal view rather than inheriting a current global montage.
- Current viewing montage is separate from acquisition montage and compatible alternatives are filtered by recorded Reference/Bipolar source labels; Trigger and Counter are excluded.
- Montage switches preserve absolute position and visible duration while replacing the complete rendered trace set.
- Playback uses monotonic wall-clock position and is independent of the UI timer interval.

## Environment limitation

Hardware-independent automated validation is complete. A manual WPF acceptance run against a real project recording was not performed in this command session because the user's Visual Studio/client process may still own the debug output. The recommended acceptance is to close any running client, launch the current Release/Debug build, open a completed project recording, seek, switch a compatible montage, and return to the project list.

## Performance scope

The regression fixture uses a one-second 4 kHz / 30-column chunk to keep CI/test disk use bounded. It verifies window-bounded reads and header-only open behavior; it is not a universal 30-minute hardware benchmark.
