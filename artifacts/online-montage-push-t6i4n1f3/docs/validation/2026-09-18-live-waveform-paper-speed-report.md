# Live Waveform Paper-Speed Validation

Date: 2026-09-18

## Behavior Locked

The live waveform uses a selected nominal paper speed in mm/s, not a fixed
seconds-per-screen control. Visible seconds are derived from the canvas width
and speed. The default is 30 mm/s, which is nominally one second per 30 mm.
Paper speed is a local display preference only; it does not change sampling,
sample-counter time, raw V/float64 persistence, filter sessions, or analysis.

## Automated Verification

- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  66 passed.
- `dotnet build desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  succeeded with 0 warnings and 0 errors.
- `openspec validate add-live-waveform-paper-speed --strict --no-interactive`:
  valid.
- Scoped `git diff --check`: no whitespace errors.

## Calibration Boundary

The layout calculation is independent of pixel resolution and Windows scaling,
but it is not a ruler-accuracy guarantee for arbitrary monitors. Exact physical
paper output requires a later per-monitor ruler calibration feature.
