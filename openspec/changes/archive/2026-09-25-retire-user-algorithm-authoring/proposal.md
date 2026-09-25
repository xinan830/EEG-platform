# Retire user algorithm authoring

## Why

User-defined algorithm Runs are retired, but the backend still accepts definition
creation, publishing and scalar preview, while the frontend still offers those
actions. A user can create an algorithm that cannot be run against EEG.

## Goal

Make official Runtime algorithms the only new executable algorithms. Keep
historical definitions, Runs, result summaries, provenance and artifacts
readable without executing retired user-defined code.

## Scope

- Retire public definition mutation and preview endpoints with a stable `410`
  response; retain read-only historical definition endpoints.
- Remove user algorithms from the active catalog and creation surfaces.
- Preserve historical Run and result retrieval and export.
- Remove the obsolete legacy analysis creation route; retain historical reads.
- Keep official Definition installation internal and unchanged.

## Non-goals

- No scientific formula, parameter, unit, quality, or result change.
- No schema migration or deletion of historical rows and artifacts.
- No removal of waveform playback or recording-backed PSD/STFT APIs.
