# Design

## Context
See proposal.md. Imported channel snapshots already survive desktop transport, database storage and export as JSON. Standard names are currently looked up independently by interpolation and classification.

## Goals / Non-Goals
Add one optional ElectrodeGeometry object to the existing channel snapshot. Do not add a database column, UI editor, dataset-specific aliases or auxiliary-channel guesses. Samples remain float64 V in source order on the original clock.

## Decisions
- One scientific validator owns schema, finite head-frame meter coordinates, unique native/name/position checks and fingerprint. Native labels must match the immutable snapshot. Head coordinates are explicit; external conversion records its units, axis transform and source hash rather than guessing inside production.
- Service adapters resolve the snapshot for imported and WPF recordings and attach geometry to scientific provenance. Explicit custom geometry is authoritative, including missing sites. Standard template lookup remains only for absent geometry.
- Interpolation uses public MNE APIs and existing pre-reference order. ICLabel retains CAR, zero-mean, sampling and convergence gates. ICA previews project the same geometry through public MNE plotting; display-only manual coordinates remain independent.
- Geometry participates in signal-view identity and scientific provenance, covering prepared sources and ICA cache. Opaque snapshot transport/export avoids duplicate desktop scientific code.
- Dataset conversion verifies axis/unit conventions against EEGLAB reader implementation and records source-provided/template status; it does not claim measured digitization.

## Risks / Trade-offs
- Incorrect supplied provenance cannot be independently proven by a parser; require explicit source/transform evidence and visual axis checks.
- Custom geometry alone cannot establish classifier scientific accuracy; retain manual review and report absence of paired clean truth.

## Migration Plan
Existing snapshots have no custom field and keep their behavior. New snapshots are rejected if malformed. Original source data remain untouched; rollback code leaves opaque snapshot information intact. Test validators, public MNE equivalence, import/export preservation, cache separation, classification and dataset execution. Register touched files exceeding 400 lines without overwriting unrelated policy changes.
