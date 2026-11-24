## Context

The current estimator uses a whole-record preprocessed single-channel source and shared Welch quality checks. Dynamic scheduling is manifest-owned, while IAPF fitting and selection are module-owned. See proposal.md for motivation and specs for externally visible behavior.

## Goals / Non-Goals

**Goals:** Make estimate acceptance explicit, prevent residual noise from becoming a reported IAPF, and align dynamic output with the selected 20-second window.

**Non-Goals:** Change filtering, Welch mathematics, raw channel selection, output units, or other algorithms.

## Decisions

- Keep the existing 20% prominence ratio as the PAF threshold, apply it to a detected local peak, and require a minimum width of 1.2 frequency bins to reject single-bin spikes without rejecting a Hann-window sinusoid's main lobe.
- Gate COG independently by requiring integrated positive Alpha residual to be at least 20% of the fitted Alpha background and its participation-ratio support to span at least three effective bins. These are versioned engineering criteria, not literature-validated clinical cutoffs.
- Prefer PAF when it passes; otherwise return COG only when its independent evidence criterion passes; otherwise return unavailable. Preserve both candidate values and criterion evidence.
- Declare 20 seconds, one-second step, and `allow_warmup=false` in the IAPF manifest so catalog-driven clients and Official Run share the same policy. The runner fallback uses the manifest policy rather than a second hard-coded default.
- Bump the IAPF scientific and implementation identities. Leave shared offline spectral version unchanged.

## Risks / Trade-offs

- [The width and integrated-excess thresholds may reject genuine but broad/weak Alpha activity or accept structured noise] → Validate with deterministic synthetic peaks, no-peak noise, broad and boundary cases; record that this is engineering evidence only.
- [A complete 20-second result has higher latency] → Keep one-second refresh after the first complete window; no partial numeric estimates are exposed.
- [Historical Runs retain v4 semantics] → Keep them readable and identify new results with the v5 scientific version.

## Migration Plan

New execution uses the v5 IAPF identity and catalog default; no stored data migration is needed. Rollback restores the previous code identity for newly created Runs while preserving existing results.

## Open Questions

None for this scoped implementation. Multi-channel fusion and eye-state stratification remain separate research decisions.
