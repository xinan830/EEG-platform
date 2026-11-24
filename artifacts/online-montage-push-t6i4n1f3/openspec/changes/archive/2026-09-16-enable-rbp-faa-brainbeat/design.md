## Context

See `proposal.md`. The executable runtime currently accepts a single `channel`
field and serializes one scalar output. RBP has four scalar band shares, FAA
requires paired F3/F4 source data and a paired quality gate, and BrainBeat
requires Fz/Pz source data plus IAPF. Their shadow/reference modules already
exist, but no Run module owns their public execution.

## Goals / Non-Goals

**Goals:**

- Keep all EEG and metric calculations in backend-owned modules.
- Reuse the shared dynamic frame planner for dynamic RBP and for any future
  stateless official metric.
- Require explicit source-channel selections for paired/multi-source metrics.
- Persist sufficient output, quality, provenance and algorithm evidence to
  explain every displayed result.

**Non-Goals:**

- Do not turn the legacy realtime BrainBeat EMA session into a normal offline
  sliding-window Run. A separately named realtime capture change is required
  for that stateful pipeline.
- Do not silently substitute current viewer channels for F3/F4 or Fz/Pz.

## Decisions

### RBP is a multi-value result, not a fake scalar

The RBP runtime calculates the four standard bands from one selected raw
channel and returns all four backend values in a `band_share` result extension.
The primary scalar output is intentionally `null`; the serializer recognizes
the extension and provides a `band_share` chart and table. This prevents a
misleading choice such as reporting Alpha RBP as if it were the RBP algorithm.

Alternative considered: create four separate official algorithms. Rejected:
it duplicates identical PSD work and hides the fact that the four values share
one denominator and one quality gate.

### FAA uses explicit pair fields and its existing paired contract

FAA Run configuration has `f3_channel` and `f4_channel` instead of mapping
names. The backend reads those raw source channels, invokes the frozen FAA
implementation and returns `ln(P_F4) - ln(P_F3)` with both alpha powers and
paired-quality evidence. It is static-only in this change because the frozen
algorithm requires at least ten overlapping two-second clean epochs; allowing
short playback windows would create a different algorithm.

Alternative considered: force FAA through the shared 4-second PSD planner.
Rejected: that would discard FAA's paired-epoch and minimum-clean-epoch
semantics.

### BrainBeat remains shadow-only in this change

BrainBeat's public documented implementation is a stateful realtime pipeline:
it includes IAPF locking and EMA warm-up. An offline Run cannot safely claim
the same semantics by applying its single-frame formula to arbitrary history.
This change supplies the reusable typed computation and evidence boundary but
does not mark BrainBeat runnable. A later change must define recording-session
state, reset, clock and EMA persistence before public enablement.

Alternative considered: enable a stateless offline approximation now.
Rejected: it would share a name with a distinct realtime algorithm and mislead
users about numerical equivalence.

## Risks / Trade-offs

- [Users expect all three to become runnable immediately] -> RBP and FAA can
  become runnable under their real contracts; BrainBeat remains explicit
  `shadow_validation` until its stateful lifecycle is defined.
- [Paired channels may be absent] -> return a structured missing-source error;
  never infer channels by label alias or column position.
- [FAA differs from the PSD panel] -> debug evidence labels FAA's own paired
  2-second FFT quality contract rather than falsely reporting Welch evidence.

## Migration Plan

1. Add tests that characterize frozen RBP/FAA results and catalog state.
2. Add typed runtime modules and extend only the official Run configuration
   needed for explicit source channels.
3. Add serializer/result-card support for the RBP multi-value extension.
4. Enable RBP then FAA in the registry/catalog after their tests pass.
5. Retain BrainBeat as shadow-only; its catalog wording explains this scoped
   limitation. A rollback simply restores the manifest availability flags; no
   source EEG or historical Run is modified.
