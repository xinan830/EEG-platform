# Implementation Tasks

## Contract and inventory

- [x] Confirm the authoritative PSD and band-power primitive APIs and their
  current scientific versions without changing their numerical behavior.
- [x] Define and freeze PeakFrequency tie, edge, interpolation, and
  insufficient-bin semantics.
- [x] Define and freeze BandRatio overlap, denominator-zero, and unavailable
  semantics.
- [x] Define stable parameter schemas, reason codes, output schemas, and
  manifest identities for both modules.

## Implementation

- [x] Implement `peak_frequency` as an official Runtime module with typed config,
  static/dynamic runners, quality evidence, and catalog registration.
- [x] Implement `band_ratio` as an official Runtime module with typed config,
  static/dynamic runners, quality evidence, and catalog registration.
- [x] Reuse scientific gateways and shared execution windows; do not add science
  branches to generic services or duplicate PSD/band-power formulas.
- [x] Persist complete parameter, coordinate, preprocessing, primitive, quality,
  and output-schema provenance for every Run.
- [x] Add API catalog and Run request compatibility coverage.

## Validation

- [x] Add golden fixtures for scalar values, units, axes, coordinates, and
  unavailable outcomes.
- [x] Add independent-reference tests for both modules with explicit `rtol` and
  `atol`.
- [x] Test static, dynamic, and box-selection equivalence and point evidence.
- [x] Test invalid bands, Nyquist violations, insufficient bins, recording gaps,
  non-finite input, zero denominator, and channel-order preservation.
- [x] Test that IAPF and standard Theta/Beta metadata remain unchanged and are
  not relabeled as the generic modules.
- [x] Run backend tests, OpenSpec strict validation, file-size policy, and
  `git diff --check` before implementation completion.
