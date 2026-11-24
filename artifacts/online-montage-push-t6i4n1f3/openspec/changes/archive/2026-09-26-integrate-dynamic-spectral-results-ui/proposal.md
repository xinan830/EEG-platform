## Why

The backend now produces structured static and dynamic PSD/STFT results, but
the frontend still understands only scalar metric summaries. Users can submit
dynamic spectral Runs, yet the result workbench cannot identify their output
kind, axes, units, window states, or artifact-backed data.

## What Changes

- Add typed frontend models for structured spectral Run summaries and artifact
  metadata.
- Expose a backend artifact-backed preview endpoint that returns bounded,
  backend-produced values for visualization without frontend recomputation.
- Render PSD dynamic frequency-series previews and STFT dynamic time-frequency
  previews with explicit axes, units, window state, and unavailable handling.
- Keep large matrices in immutable artifacts; previews are bounded views and
  are never treated as the scientific source of record.
- Preserve existing scalar metric and static spectrum result rendering.

## Impact

The change affects the result API, frontend result types, result workbench, and
spectral chart components. It does not change PSD/STFT formulas, Runtime
identity, raw recordings, or artifact contents.

## Non-Goals

- Frontend calculation of PSD, STFT, dB, downsampling, or quality states.
- Replacing NPZ artifact export or changing scientific units.
- Adding new algorithms or changing dynamic window policy.

## Risks And Rollback

Preview payloads must remain bounded so a long recording cannot freeze the UI.
The backend will select/limit the preview and preserve artifact identity. The
feature can be rolled back at the API and result-workbench boundary without
changing stored Runs or artifacts.
