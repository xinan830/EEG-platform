## 1. Specification And Baselines

- [x] 1.1 Validate the change strictly before implementation.
- [x] 1.2 Add characterization tests for RBP four-band values, FAA paired
  quality and catalog availability.

## 2. RBP Runtime

- [x] 2.1 Add the RBP runtime module, config and manifest for its static
  four-band contract.
- [x] 2.2 Extend official Run serialization and result rendering for a
  backend-owned four-band RBP result.
- [x] 2.3 Enable RBP only after static, quality-null and artifact tests
  pass.

## 3. FAA Runtime

- [x] 3.1 Extend official Run configuration with explicit paired FAA sources
  without changing IAPF or Theta/Beta request behavior.
- [x] 3.2 Add the static FAA runtime module and paired-quality evidence.
- [x] 3.3 Enable FAA only after missing-channel, identical-channel, quality,
  provenance and frozen-formula tests pass.

## 4. BrainBeat Boundary And Delivery

- [x] 4.1 Preserve BrainBeat's disabled state and add a catalog regression
  test documenting its realtime EMA/IAPF-lock boundary.
- [x] 4.2 Update user-facing algorithm documentation and OpenSpec status.
- [x] 4.3 Run strict OpenSpec validation, backend pytest, frontend Vitest,
  type check/build and `git diff --check`.
- [x] 4.4 Produce a validation report and archive the completed change.
