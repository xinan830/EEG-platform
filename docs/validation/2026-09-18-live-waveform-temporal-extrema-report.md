# Live Waveform Temporal-Extrema Validation

Date: 2026-09-18

## Defect Addressed

The former screen-density renderer emitted a bucket minimum and maximum at the
same x coordinate. This created an artificial vertical envelope and a visibly
stepped waveform. It did not change raw EEG or filtering, but it was a poor
presentation of the received signal.

## Behavior Locked

Each visible bucket now retains at most four real samples: first, minimum,
maximum, and last. Duplicates are removed and the retained samples are emitted
in sample-counter order. No output uses a fabricated interpolation or average.

## Automated Verification

- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  67 passed.
- `dotnet build desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  succeeded with 0 warnings and 0 errors.
- `openspec validate preserve-temporal-extrema-in-live-waveform --strict --no-interactive`:
  valid.
- Scoped `git diff --check`: no whitespace errors.
