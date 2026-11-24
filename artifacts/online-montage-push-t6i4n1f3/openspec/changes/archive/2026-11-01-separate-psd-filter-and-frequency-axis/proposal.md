# Separate PSD Filter and Frequency Axis

## Why

PSD currently passes one low/high frequency pair through both preprocessing and
output clipping. Dynamic PSD needs a stable display axis while retaining the
user-selected high-pass, low-pass, and notch settings. These are different
scientific concerns and must be represented separately.

## Scope

- Add explicit filter and output frequency ranges to the spectral loading
  boundary.
- Make official dynamic PSD use the configured filter range and an explicit
  fixed output axis of 1-50 Hz.
- Keep static PSD output on the requested range.
- Keep existing non-PSD callers behavior-compatible during migration.

## Non-goals

- No change to Welch mathematics, quality thresholds, or frequency resolution.
- No change to trend playback or acquisition integration.
