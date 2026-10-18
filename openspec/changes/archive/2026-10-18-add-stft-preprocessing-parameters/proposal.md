# Add STFT preprocessing parameters

STFT currently exposes a separate parameter card but its backend execution uses
the historical fixed 1-30 Hz preprocessing path. This change makes the shared
frequency and notch controls real STFT run inputs while preserving the existing
default contract of legacy recording endpoints.

## Scope

- STFT official Runs accept the selected analysis low/high frequency and notch
  (`关闭`, `50 Hz`, or `60 Hz`).
- The selected range is applied to preprocessing and to the returned STFT
  frequency axis.
- The selected values are retained in execution evidence and provenance.
- WPF reuses the existing shared frequency controls instead of creating a
  second, divergent parameter model.

The existing non-official/configured spectrogram endpoint keeps its historical
1-30 Hz default when no range is supplied.
