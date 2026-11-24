## Why

The channel configuration identifies EEG inputs by electrode label, but it does not preserve where those electrodes sit on the scalp. A future topographic display needs a stable, reviewable spatial mapping so it can place values without guessing from labels or silently changing the meaning of historical recordings.

## What Changes

- Resolve supported standard 10–20 scalp-site metadata from the backend's existing MNE standard montage data and expose the resolved site in channel settings; do not maintain a duplicate hand-copied standard coordinate table.
- Preserve the position mapping in saved configuration and recording snapshots, and include it in semantic fingerprints so historical configurations remain identifiable.
- Preserve resolved coordinate snapshots and their coordinate convention/source version in configuration and recording snapshots; validate known, unknown, duplicate, and non-electrode labels explicitly. Unknown positions remain unavailable rather than receiving guessed coordinates.
- Keep topographic interpolation, topomap values, reference transformations, and bipolar-derived scalp values out of this phase. Permit user-placed 2D diagram positions for custom labels, explicitly separate from scientific 3D coordinates.

This is additive for existing profile JSON: older entries load with no spatial site and remain usable for waveform display. A read-only backend resolver is added; no raw sample format changes are planned.

## Capabilities

### New Capabilities
- `desktop/electrode-locations`: Standard scalp position mapping and persistence for channel configurations.

### Modified Capabilities
- None.

## Impact

- Affected code: `Modules/Channels` domain, validation, view model and settings view; configuration fingerprinting and recording snapshot serialization; focused desktop tests.
- No scientific values or signal processing change. Coordinates are display metadata in a declared coordinate convention and do not infer electrical reference or convert bipolar derivations into electrode potentials.
- Existing profile files remain readable; a missing location is an explicit unavailable state. New snapshots retain their mapping independently of later edits to the active profile.
- Reuse the existing backend MNE dependency through a typed, read-only location-resolution API. This adds no separate coordinate package or topomap API. Engineering tests do not constitute clinical validation.
