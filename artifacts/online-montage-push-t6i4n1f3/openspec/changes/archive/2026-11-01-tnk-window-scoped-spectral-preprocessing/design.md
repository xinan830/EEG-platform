## Context

The existing generic spectral loader owns the `offline-spectral-v3`
whole-recording preparation contract. TNK dynamic windows currently reach that
loader through the recording algorithm context, so a first 4-second request can
prepare a long recording before Welch calculation begins. The recording access
layer already supports identity-bound half-open sample reads.

## Goals / Non-Goals

**Goals:**

- Give TNK dynamic spectral requests an explicit bounded preprocessing scope.
- Read only the target range plus a fixed two-second context on each available
  side, then crop back to the target range.
- Keep source identity, channel order, V/float64 values, gap rejection and
  quality gates explicit.
- Keep the generic offline spectral path and waveform path unchanged.

**Non-Goals:**

- Claim numerical equality with whole-recording zero-phase filtering at window
  edges.
- Change TNK formulas, cadence, window lengths, or WPF/API request shape.
- Add a generic short-window reader for every algorithm.

## Decisions

1. **Opt-in at the typed spectral-source boundary.** Add a bounded TNK scope to
   the existing spectral request and pass it only from TNK dynamic adapters.
   This keeps the generic loader's current contract intact and makes the
   scientific choice visible in provenance.

2. **Finite context is two seconds per side, clamped to the recording.** The
   bounded source reads `[target_start-context, target_end+context)` and filters
   that range with the existing zero-phase primitives before cropping. The
   context is deliberately fixed and versioned; it is not inferred from a
   caller or silently expanded to the complete file.

3. **Reject non-finite input before filtering.** A missing sample in the target
   or context invalidates the bounded source. No interpolation or zero fill is
   introduced, so the existing quality gate can report structured unavailable
   science.

4. **Version the scope in the result envelope.** Bounded results use
   `tnk-window-spectral-v1` in their preprocessing identity and report scope,
   context, edge policy, and actual range. They never reuse the continuous
   source cache identity.

5. **Source identity is retained without changing the public request.** The
   current recording registration already stores the immutable source hash; the
   bounded read still validates the access session before and after the sample
   read. Avoiding full scientific preparation does not relax source validation.

## Risks / Trade-offs

- [Boundary numerical difference] Finite-context zero-phase output can differ
  from whole-recording output near boundaries → record the scope and compare
  bounded versus continuous values during acceptance before treating them as
  interchangeable.
- [Context gap rejection] A gap just outside the target can invalidate a point
  because filtering would otherwise contaminate it → keep the rejection policy
  explicit and expose the structured gap evidence.
- [Repeated reads] Adjacent windows may reread overlapping context → rely on the
  existing bounded-work deduplication first; do not reintroduce a whole-file
  cache as a shortcut.

## Migration Plan

Implement the bounded source behind the new TNK scope, publish the new identity,
and keep continuous consumers on their existing identity. Rollback is selecting
the prior TNK scientific version or disabling the TNK bounded capability; stored
results remain readable because their provenance is immutable.

## Maintainability Assessment

`backend/app/algorithms/tnk/group.py` is already above the 600-line review
threshold. This change adds only the bounded-source routing and provenance
fields; it does not mechanically split the existing orchestration file. A
future split should separate replay scheduling from child calculation once the
TNK execution contract is stable.
