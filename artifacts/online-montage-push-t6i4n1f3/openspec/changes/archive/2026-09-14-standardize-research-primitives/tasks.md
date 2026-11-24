## 1. Scientific types and units

- [x] 1.1 Implement immutable scientific value, kind, unit, quality, time, channel, and provenance models with shape validation.
- [x] 1.2 Implement explicit unit compatibility and arithmetic result rules without implicit conversion.

## 2. Signal and spectral nodes

- [x] 2.1 Implement channel selection, rereference, bandpass, notch, detrend, and anti-aliased resample nodes.
- [x] 2.2 Implement deterministic window, Welch PSD, band-power, RBP, and quality-gate nodes.
- [x] 2.3 Implement safe arithmetic, ln, statistics, CV, weighted-sum, and output nodes in a closed registry.

## 3. Composition and verification

- [x] 3.1 Build RBP and FAA from primitives and verify values, units, ordered channels, quality, and provenance.
- [x] 3.2 Test invalid units, unknown nodes, immutable arrays, window centers/residual policy, resampling, and Welch parity.
- [x] 3.3 Document the primitive contracts and examples; review file-size policy.
- [x] 3.4 Pass OpenSpec strict validation, full backend/frontend gates, generate validation evidence, sync specs, and archive the change.
