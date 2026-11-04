## Context

See proposal.md for motivation. The backend already centralizes Welch, quality checks, unit conversion, band integration, and preprocessing primitives, but callers configure those primitives differently. Review filtering is a separate causal display path. Several official algorithms already serialize notch choices, while fixed-contract algorithms such as RBP and Brainbeat do not yet expose the choice uniformly. Existing persisted offline results identify `offline-spectral-v3`.

## Goals / Non-Goals

**Goals:**
- Establish one versioned backend analysis-preprocessing contract with 1–50 Hz baseline coverage and 50 Hz/Q=30 notch default.
- Keep UI display filters out of algorithm requests and scientific identities.
- Preserve module-owned windows, Welch parameters, output ranges, bands, quality policy, and scientifically fixed preprocessing where explicitly justified and traceable.
- Make notch-off an explicit user choice wherever the algorithm contract permits it.

**Non-Goals:**
- Make all algorithms emit 1–50 Hz or use identical window/Welch settings.
- Change review waveform filter behavior, raw data, rereferencing, or acquisition.
- Recalculate or rewrite historical Run artifacts.
- Claim clinical validity from engineering tests.

## Decisions

- Keep preprocessing and spectral estimation as separate declarations: shared preprocessing coverage is 1–50 Hz by default, while output clipping and algorithm target bands remain algorithm-owned.
- Use explicit `notch_hz` values (`50`, alternate only if the algorithm declares it, or `null` for off); never infer the notch from the requested output range.
- Introduce `offline-spectral-v4`; keep `offline-spectral-v3` frozen and include effective preprocessing parameters and v4 identity in cache/run identity.
- Preserve zero-phase preprocessing for offline analysis and record phase, filter range, notch frequency/Q, unit, ordered channels, time range, and estimator contract in provenance.
- The 50 Hz notch intentionally attenuates power around 50 Hz, including RBP gamma power near its upper edge; result evidence must disclose it. Notch-off runs remain available to users where compatible.
- Update the desktop algorithm parameter contract only for analysis settings; do not bind review display filter view-model state into run serialization.

## Risks / Trade-offs

- **Existing 1–30 Hz algorithm contracts may produce changed results when moved to the shared 1–50 Hz preprocessing baseline** → retain v3 as historical, move current consumers to v4 with algorithm-owned output ranges, and update module scientific versions and independent reference tests for changed output.
- **A 50 Hz notch changes gamma-band estimates near 50 Hz** → expose notch-off and retain exact setting/Q in provenance.
- **Sampling rates at or below 100 Hz cannot support a 50 Hz edge** → return structured unavailable/validation results; never silently clip.
- **Old and new results could be confused or reused** → version cache/run identities and keep historical results readable as historical evidence only.

## Migration Plan

1. Inventory each official spectral consumer and record its current filter, notch, estimator, window, bands, and output range.
2. Introduce `offline-spectral-v4` while preserving the frozen v3 implementation/identity for historical records; route current consumers through v4, retaining explicit algorithm-specific output ranges.
3. Wire user-selectable notch state through desktop request schemas and backend snapshots; verify it is absent from the display-filter flow.
4. Update independent numerical references and contract tests; retain old artifacts without rewriting them and reject old cache entries for new-contract requests.
5. Roll back by reverting the current contract implementation/version together; do not delete old Run artifacts or raw recordings.

## Open Questions

None.
