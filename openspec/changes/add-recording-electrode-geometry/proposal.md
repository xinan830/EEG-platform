# Proposal

## Why
Imported EEG labels can name a different physical site than an identically named system-template electrode. Recording-bound geometry must reach spatial preprocessing without guessing sites or auxiliary physiological identities.

## What Changes
- Preserve validated optional ElectrodeGeometry inside the existing immutable channel-configuration snapshot, including native input bindings, SI coordinates, frame and provenance.
- Share that geometry across bad-channel inspection/interpolation, ICA classification and component scalp maps; include it in scientific identities.
- Keep existing standard-template behavior when geometry is absent. An explicit custom geometry never falls back to standard-name guesses.

## Capabilities
### New Capabilities
- `signal/recording-electrode-geometry`: Source-bound scientific electrode geometry and spatial-consumer consistency.
### Modified Capabilities
None. Existing display-only template metadata remains independent.

## Impact
Backend scientific geometry contract, snapshot import validation, recording access/provenance, interpolation, ICA and preview; desktop opaque snapshot preservation and export checks. No database migration, dependencies, new coordinate editor, resampling, filter changes or EXG reclassification. Existing snapshots without geometry remain readable; invalid supplied geometry is rejected. Rollback preserves original recordings and snapshots.
