## Why

Runs and validation records are traceable in SQLite, but users cannot inspect a
uniform result representation or export a self-describing package for another
researcher to review.

## What Changes

- Add a backend result view for scalar summaries, time series, PSD, spectral
  matrices, channel mappings, artifacts, units, and quality state.
- Add export packages: manifest JSON, result JSON, CSV tabular summaries, NPZ
  artifacts, and persisted validation report JSON.
- Add validation-center endpoints for synthetic engineering evidence and
  pointwise comparison, explicitly separate from clinical interpretation.
- Add a compact frontend results drawer that only renders returned data.

## Non-Goals

Parquet, PDF, participant PII exports, clinical conclusions, and browser-side
EEG calculations remain excluded.
