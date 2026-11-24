# Dynamic Spectral Results UI Design

## Backend preview boundary

`GET /api/runs/{run_id}/structured-preview` returns only for a completed or
quality-partial structured spectral Run. The response includes the Run and
artifact identity, output kind, channel order, axes metadata, units, window
rows/states, and a bounded backend-selected preview. It rejects non-structured
Runs and invalid channel/window requests.

The preview limit is enforced by the backend. The endpoint never changes the
artifact and never writes a new Run. It reads and verifies the artifact
checksum before returning values. Any unavailable matrix cell remains `null`;
the frontend must not replace it with zero.

## Frontend rendering

- Frequency-series previews show frequency in Hz, declared power unit, selected
  window, and state label.
- Time-frequency previews show inner time in seconds, frequency in Hz, declared
  display unit, and rejected/unavailable cells as blank/neutral cells.
- Window state counts and the selected window's sample range are visible beside
  the chart.
- Static scalar metrics and legacy spectrum/spectrogram responses keep their
  current components.

The frontend formats backend values only. It does not perform unit conversion,
FFT, Welch, STFT, interpolation, quality classification, or downsampling.

## Failure behavior

Artifact checksum failure, unavailable Run, unsupported output, or preview limit
failure produces a scoped result error. The result workbench keeps the Run
provenance and export link visible so the user can reproduce the issue.
