## Context

The existing live display bridge sends bounded, sample-major V/float64 batches
to a local Python service. It owns filter state per acquisition session; the
desktop owns raw recording and rendering. See `proposal.md` for the motivation.

## Goals / Non-Goals

**Goals:**

- Add a validated optional notch to the existing causal display filter.
- Preserve units, channel order, and unmodified raw recording data.
- Make the runtime behavior observable through a numeric regression test.

**Non-Goals:**

- This does not add a new scientific preprocessing pipeline, modify BDF/EDF
  output, or enable filter changes while recording.
- This does not claim clinical-grade mains suppression or replace a documented
  offline preprocessing decision.

## Decisions

- Python remains the single implementation of notch and band-pass filtering.
  C# sends configuration and renders returned batches, preventing duplicate
  scientific formulas. An alternative C# notch was rejected because it would
  drift from the Python analysis stack.
- The allowed notch values are disabled, 50 Hz, and 60 Hz. Restricting choices
  prevents a free numeric field from accepting invalid or unreviewed filters.
- The notch is applied before the causal band-pass. It only sees EEG reference
  and bipolar columns; trigger and sample-counter columns pass through exactly.

## Risks / Trade-offs

- [Real-time filter state can make a waveform depend on prior samples] → The
  state is scoped to one live session and settings are locked during recording.
- [Python service becomes unavailable] → Raw acquisition continues; the
  desktop visibly degrades to unfiltered rendering rather than fabricating
  filtered values.
- [A 50/60 Hz notch removes nearby signal] → It is optional, clearly labelled
  as display-only, and must not be substituted for offline scientific choices.

## Migration Plan

1. Create a new live filter session with an optional `notch_hz` value.
2. Older callers can omit it and receive the previous no-notch behavior.
3. Rollback is configuration-level: choose disabled notch or deploy the prior
   service; raw files require no migration because they were never modified.
