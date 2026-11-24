# Design

## Context

See `proposal.md` for motivation and `specs/desktop/acquisition-preview-montage-switch/spec.md` for observable behavior. Today `AcquisitionWorkspaceViewModel` owns the transient montage, `LiveMonitoringViewModel` applies it to waveform projection, and `LiveSpectralAnalysisViewModel` independently derives algorithm channels from stream metadata. Acquisition batches retain raw sample-major values, hardware channel order, V units, and sample counters.

## Goals / Non-Goals

**Goals:** Give one acquisition-session owner to the selected montage and revision, and make both realtime consumers use an immutable captured snapshot. Preserve exact sample coordinates, raw persistence, existing backend algorithms, and per-consumer preprocessing.

**Non-Goals:** Share the offline review coordinator or its lifecycle; move scientific formulas to WPF; change recording files; persist live results; change algorithm windows, rolling cadence, or chart styling.

## Decisions

- `ConfiguredAcquisitionRuntime` owns a small `LiveMontageCoordinator` for the session lifetime. It accepts only profiles compatible with the frozen acquisition channel-configuration fingerprint, initializes from the recording profile, and exposes the current immutable profile plus monotonic revision. The workspace ViewModel remains the interaction surface and delegates selection/validation to this owner.
- Montage changes serialize with acquisition batch publication at the existing batch boundary. Each accepted batch captures one revision and profile for both waveform projection and live-analysis dispatch; sample counter is the authoritative boundary. Do not infer time from UI timers or transport arrival time.
- The analysis path keeps raw values and raw channel order intact at acquisition and storage boundaries. The existing live-analysis adapter applies the selected montage using backend-owned montage/reference primitives before algorithm-specific preprocessing. Algorithm formulas and window scheduling remain in their existing backend modules. If the backend cannot resolve a profile or required channel role, it returns an explicit unavailable/failure result; no client-side formula or positional fallback is allowed.
- Algorithm configuration and results carry the montage revision/fingerprint for live-session identity. On revision change, retire/reset affected live algorithm buffers so a scientific window cannot mix montage outputs; reject late results against the active revision. The exact smallest wire metadata change is decided after tracing the current live API and its consumers.
- The traced live API currently creates one raw-channel source (`channel_names`, `channel_units`) and appends sample-major float64 V values with only `first_sample_counter` and `sample_count`. Existing live RBP tests prove requested channels are resolved directly against that raw source. The live protocol therefore needs a typed montage snapshot at session start and a revision/effective sample boundary on each montage change; sample bodies remain raw and unchanged. The backend projects requested derived channels before algorithm preprocessing and resets active rolling windows at the revision boundary. It must validate the supplied profile against the session's raw channel labels and reject unavailable mappings.
- Display filters remain waveform-only. Algorithm preprocessing remains governed by each algorithm's existing configuration; the shared coordinator supplies montage identity and ordered signal channels, not a shared filter state.

## Risks / Trade-offs

- [A new revision can race an in-flight window] → Serialize profile publication with batch dispatch, reset the affected live source at the sample-counter boundary, and reject late results by session and revision.
- [A montage may not provide an algorithm's required channel kind or spatial locations] → Validate declared inputs before enablement and report unavailable with a reason; topography remains available only when the selected montage supplies valid electrode coordinates.
- [Passing montage metadata may require a live protocol update] → Keep it scoped to the realtime API, version the message explicitly, update all known consumers and contract tests, and leave review/recording contracts unchanged.
- [Concurrent recording could accidentally capture the display profile] → Keep writer creation bound to the immutable preparation snapshot and assert it in acquisition tests.

## Migration Plan

No stored recording migration is required. Initialize each new acquisition session's live revision from its frozen acquisition montage. Rollback removes the session coordinator and restores current behavior; raw recording and archived results remain readable because their contracts do not change.

## Testing Strategy

Add unit coverage for compatible/incompatible assignment, monotonic revisions, batch-boundary capture, preserved raw value order/counters, and stale result rejection. Add a live bridge contract test proving one captured revision and ordered montage metadata reach the backend. Run the focused desktop and backend live-analysis tests, isolated desktop build if normal output is locked, `git diff --check`, and strict OpenSpec validation. Hardware rendering remains a separate acceptance check.
