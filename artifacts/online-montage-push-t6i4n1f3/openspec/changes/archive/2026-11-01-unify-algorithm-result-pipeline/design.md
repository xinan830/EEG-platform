## Context

The current desktop path already has shared Run identity checks and a full-run window adapter, but output-array selection, units, provenance compatibility, and algorithm presenters still encode overlapping rules. PSD is a one-dimensional frequency series while STFT is a time-frequency matrix, so their scientific projections must remain separate. The shared contract must therefore normalize transport and integrity behavior without hiding shape or time semantics.

## Goals / Non-Goals

**Goals:**

- Make full-run window access a single validated pipeline driven by registered output descriptors.
- Preserve absolute sample coordinates, validated seconds, channel order, units, quality states, and provenance.
- Make legacy compatibility explicit, bounded, and observable.
- Keep the existing API and scientific formulas compatible during incremental migration.

**Non-Goals:**

- Rewriting PSD, STFT, or any backend numerical algorithm.
- Moving scientific computation into WPF or recomputing values in presenters.
- Combining PSD and STFT arrays into one shape or inventing a universal chart model.
- Changing the on-demand execution scheduler or waveform review pipeline.

## Decisions

1. **Descriptor registry at the shared boundary.** Add a typed descriptor keyed by the backend output contract, not by UI page. It declares scientific array names, axis names, units, expected rank/shape, time basis, channel-order behavior, required provenance, and legacy compatibility versions. The shared reader consumes the descriptor; the generic Run executor remains algorithm-ID agnostic.

2. **One validation pass, indexed reads.** Validate artifact identity, metadata, window geometry, array row counts, and descriptor shape once when the structured preview is loaded. Build an immutable window index keyed by absolute sample range. Cursor changes only select an indexed row and construct the common response.

3. **Common response plus module projection.** The shared reader returns the existing dynamic-window response with normalized state, range, quality, failure, axes, units, and provenance. PSD and STFT presenters continue to project their declared arrays into chart-specific models.

4. **Explicit legacy adapter.** Compatibility is selected by artifact/schema version and descriptor rules. It may add only deterministic metadata whose meaning is established by stored geometry and the backend contract. It must emit a diagnostic source/version marker; otherwise it returns structured unavailable/integrity failure.

5. **Incremental migration.** Migrate PSD and STFT first, retain the current adapter as a compatibility façade during rollout, then migrate other algorithms. Existing serialized fields remain accepted; new descriptor fields are additive. Rollback is deleting descriptor registration for an algorithm and using the existing indexed/on-demand fallback, without modifying stored artifacts.

## Risks / Trade-offs

- [Risk] A descriptor can incorrectly describe a matrix shape and make invalid data appear usable → validate rank, axis lengths, channel order, and units against stored metadata and add malformed-fixture tests.
- [Risk] Legacy artifacts lack enough provenance → fail explicitly with a structured reason; never synthesize numerical values.
- [Risk] A shared adapter grows into an algorithm switch → keep algorithm-specific behavior behind descriptor callbacks/validators and reject registrations that omit required semantics.
- [Risk] Migration touches uncommitted performance work → isolate commits/files, compare the active long-recording change before editing shared scheduling code, and run focused tests first.

## Migration Plan

1. Add the descriptor types, registry, and shared validation/indexing tests without changing current behavior.
2. Register PSD and STFT descriptors and route their full-run previews through the shared reader; retain the old façade for response compatibility.
3. Add legacy artifact fixtures, structured diagnostics, and GUI acceptance checks for cursor seeks, partial/rejected windows, and reused Run identities.
4. Migrate additional algorithms only after their descriptors pass the same contract suite.
5. Remove duplicate per-algorithm transport/compatibility branches after telemetry shows no fallback regressions.

## Open Questions

None that change the planned scope or contract.
