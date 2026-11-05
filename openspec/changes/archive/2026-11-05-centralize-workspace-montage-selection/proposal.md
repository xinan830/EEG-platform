## Why

Montage selection is currently represented by separate state in review, algorithm, and acquisition view models. In the recording-review workflow, waveform display and algorithm execution must follow one deliberate user selection; copying changes between selectors can leave the displayed signal and scientific input on different montage snapshots.

## What Changes

- Establish one writable, workspace-scoped montage selection as the authority for each active recording-review workspace.
- Make waveform display and every review algorithm panel consume that same selected montage identity and immutable snapshot.
- Keep waveform processing and scientific algorithm execution as separate pipelines; algorithm-specific input roles remain algorithm-owned and resolve through the shared montage.
- Remove independently writable montage selections and event-copy synchronization from review consumers; montage changes publish a new selection revision that invalidates stale waveform and algorithm work.
- **BREAKING** Replace the review workflow's independent display/analysis montage choice with one shared selection. Preserve acquisition montage snapshots and completed historical results as immutable provenance.

## Capabilities

### New Capabilities

- `desktop/workspace-montage-selection`: Workspace-scoped ownership and consumption of one selected montage by review signal and algorithm paths.

### Modified Capabilities

- `signal/montage-reference`: Replace the review workflow's independent display/analysis selection behavior while preserving immutable acquisition and analysis snapshots.
- `desktop/algorithm-montage-selection`: Algorithm panels in a review workspace consume the workspace-selected montage rather than maintaining a separate selectable montage.
- `desktop/wpf-official-algorithms`: Official algorithm requests derive montage identity and channel mappings from the shared workspace selection revision.

## Impact

- Desktop workspace composition and the Recording Review, waveform, and algorithm-panel view models/services.
- Montage-aware waveform and algorithm request identities, cancellation/stale-result handling, and focused desktop contract tests.
- No scientific formula, raw sample, unit, time-coordinate, backend algorithm, or database schema change is intended. Request/provenance inputs remain explicit and immutable per execution.
- Migration is an in-memory ownership change: old independent review selectors and synchronization code are removed, not retained as compatibility paths. Existing saved acquisition snapshots and completed Runs remain unchanged.
- Non-goals: merging waveform and algorithm computation pipelines; changing acquisition-time montage selection; changing algorithm-specific channels/semantic roles; changing how montage formulas are defined.
