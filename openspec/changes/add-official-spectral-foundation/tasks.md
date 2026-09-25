# Official algorithm development tasks

This is the only active task list for the official algorithm phase.
Checked items are completed in the current repository or by a cited archived
change. Unchecked items are the remaining implementation scope.

## 0. Baseline and governance

- [x] Confirm the canonical science boundary in `app/scientific`.
- [x] Confirm the Runtime composition root and closed official registry.
- [x] Confirm user-defined executable algorithms are retired for new Runs.
- [x] Record the existing PSD, STFT, IAPF, RBP, FAA, Theta/Beta, PeakFrequency,
  and BandRatio changes as historical evidence, not active work queues.

## 1. Scientific contract freeze

- [x] Use SI units internally: EEG `V`, PSD `V^2/Hz`, band power `V^2`,
  frequency `Hz`, and time `s`.
- [x] Use recording-relative `[start_sample, end_sample)` as the canonical
  scientific coordinate; derive seconds from the validated sampling rate.
- [x] Separate generic mathematical legality from official preset semantics.
- [x] Represent quality as DataIntegrity, SignalQuality, and AlgorithmValidity.
- [x] Represent dynamic points as Partial, Complete, Rejected, or Unavailable.
- [x] Distinguish recording gaps from transform zero-padding.
- [x] Use typed output schemas instead of a single module-level output unit.
- [x] Keep timebase, paper speed, sensitivity, zoom, and chart state outside
  scientific Run identity.

## 2. Reusable scientific primitives

- [x] Provide the explicit SI-unit FFT implementation primitive with padding
  evidence; it is not a user-facing algorithm.
- [x] Preserve the migrated Welch PSD contract and independent reference tests.
- [x] Preserve band-power integration and boundary interpolation semantics.
- [x] Preserve the migrated STFT/spectrogram contract and quality rows.
- [x] Preserve sample-aligned window construction for static and dynamic Runs.
- [x] Add a typed structured result contract for frequency-series output.
- [x] Add a typed structured result contract for time-frequency matrix output.

## 3. Runtime execution model

- [x] Resolve and validate typed module configuration before loading EEG data.
- [x] Execute static analysis from one canonical sample range.
- [x] Execute dynamic analysis from one range, window, and step contract.
- [x] Route box selection through the same static execution path.
- [x] Attach sample coordinates, actual ranges, quality, and primitive evidence.
- [x] Persist structured frequency/time axes through the Runtime artifact path.
- [x] Persist large spectral matrices as immutable artifacts rather than Run
  summaries or frontend state.

## 4. Official Runtime modules

- [x] Register and validate PeakFrequency as a generic parameterized module.
- [x] Register and validate BandRatio as a generic parameterized module.
- [x] Register and validate IAPF with its official Alpha/aperiodic contract.
- [x] Register and validate individualized Theta/Beta.
- [x] Register and validate RBP with its multi-field output schema.
- [x] Register and validate FAA with its paired-channel contract.
- [x] Add a registered official PSD/frequency-series module using the frozen
  scientific spectral gateway.
- [x] Add a registered official STFT/time-frequency module using the frozen
  spectrogram gateway.
- [x] Expose the PSD module through the official catalog with stable scientific
  and implementation identities.
- [x] Expose the STFT module through the official catalog with stable scientific
  and implementation identities.

## 5. Provenance and API integration

- [x] Preserve Definition identity, scientific version, implementation
  identity, configuration hash, and Run lifecycle provenance.
- [x] Preserve explicit channel, reference, filter, window, and quality
  evidence for existing official algorithms.
- [x] Define structured serialization for PSD frequency axes, channel order,
  units, and artifact references.
- [x] Define structured serialization for STFT frequency axis, time centers,
  matrix shape, linear power, display power, and quality rows.
- [x] Ensure API responses expose backend-returned arrays without frontend
  recomputation.

## 6. Validation

- [x] Keep independent numerical references for existing spectral contracts.
- [x] Test invalid channels, invalid bands, Nyquist violations, quality gates,
  unavailable values, dynamic states, and provenance for existing modules.
- [x] Add PSD module golden and independent-reference tests.
- [x] Add STFT module golden and independent-reference tests.
- [x] Add structured artifact round-trip and checksum tests.
- [ ] Add static/dynamic/box-selection equivalence tests for the new modules.

## 7. Completion gates

- [x] Backend full test suite passes.
- [x] Independent spectral validation passes with explicit tolerances.
- [x] OpenSpec strict validation passes.
- [ ] File-size policy and `git diff --check` pass.
- [x] Validation report records identities, ranges, units, quality, artifacts,
  and rollback point.
- [ ] This change is archived only after every unchecked task is complete.
