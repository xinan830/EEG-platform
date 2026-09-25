# Migration Tasks

## 0. Baseline and freeze

- [x] Record the current full backend test baseline and the relevant golden
  scientific values before moving any active implementation.
- [x] Produce an ownership inventory for every `app/eeg_core` file: active
  scientific authority, compatibility adapter, validation reference, or
  retired code.
- [x] Mark `eeg_core` frozen for new active scientific functionality; document
  the temporary exception path for compatibility fixes.
- [x] Verify source-size policy records for files affected by migration.

## 1. Guard the architecture

- [x] Add dependency architecture tests for the allowed and forbidden imports
  defined in the design.
- [x] Add a `bootstrap` composition root that imports concrete official modules
  and registers them through `algorithm_runtime`; prove Runtime itself has no
  concrete-algorithm imports.
- [x] Add a single explicit built-in module registration composition root test.
- [x] Add a duplicate-authority test for PSD, band power, IAPF, RBP, FAA, and
  Theta/Beta.
- [x] Add tests proving validation/reference implementations are not active
  Runtime execution paths.
- [x] Add a machine-readable `ScientificAuthority` manifest with authority,
  delegate, executable, and reference-only fields, and make architecture tests
  consume it.

## 2. Establish scientific contracts and quality boundary

- [x] Create typed `scientific` contracts for SI units, sample ranges,
  preprocessing snapshots, quality layers, primitive evidence, and output
  schemas in alignment with `add-official-algorithm-parameter-contract`.
- [x] Migrate shared data-integrity and signal-quality checks with thin
  compatibility adapters.
- [x] Verify gap handling, non-finite values, clipping, flatline, and
  unavailable-result semantics remain unchanged.

## 3. Migrate reusable spectral capabilities

- [x] Migrate offline preprocessing without changing filter behavior.
- [x] Migrate Welch PSD and preserve frequency bins, segment policy, units, and
  quality evidence.
- [x] Migrate band-power integration and preserve interpolation boundaries.
- [x] Migrate STFT/spectrogram primitives with explicit transform-padding
  evidence and no gap imputation.
- [x] Run golden and independent-reference comparisons for every capability
  before retiring its old authority.

## 4. Migrate official algorithms

- [x] Migrate IAPF to one active `algorithms/iapf` authority, retaining any old
  estimator only as a tested compatibility/reference adapter.
- [x] Migrate RBP, preserving its exact scientific version and result semantics.
- [x] Migrate Theta/Beta, preserving its exact scientific version and result
  semantics.
- [x] Migrate FAA to `algorithms/faa` while preserving its exact scientific
  version and result semantics.
- [x] Consolidate official catalog metadata behind Runtime manifests; remove
  parallel active catalog ownership only after compatibility tests pass.
- [x] Introduce PeakFrequency and BandRatio only in their separate approved
  scientific feature changes, not as a side effect of this relocation.

## 5. Clean services and persistence

- [x] Replace direct science imports in services with typed scientific/Runtime
  interfaces while preserving cache and response behavior.
- [x] Move the algorithm-parameter preset repository to
  `persistence/repositories`, retaining a compatibility facade and preserving
  its table and serialization semantics.
- [x] Move Definition, Run, Validation, and algorithm-preset repositories to
  `persistence/repositories`, preserving SQL behavior and existing import
  adapters.
- [x] Verify queue recovery, cancellation, artifact integrity, and idempotency
  after every persistence move.

## 6. Retire user-defined execution safely

- [x] Keep the historical user-defined catalog entry visible with
  `status=retired`, `executable=false`, `creatable=false`, and
  `editable=false`; reject new creation/execution with a stable unavailable
  response instead of deleting the entry.
- [x] Keep historical definition and Run reading available without importing the
  user-definition executor.
- [x] Move user-definition execution behind an explicit legacy boundary.
- [x] Propose any physical deletion in a separate removal change only after
  historical-read and architecture tests pass.

## 7. Complete and verify

- [ ] Remove compatibility adapters only after all active callers migrate and
  no architecture guard depends on them.
- [x] Freeze versioned scientific baseline fixtures and record exact comparison
  rules for discrete/identifier/unit/coordinate/provenance fields plus named
  per-output `rtol`/`atol` for floating-point values and arrays.
- [x] Verify every migrated capability against its frozen baseline and an
  independent reference, including failure evidence for coordinate shifts and
  tolerance violations.
- [x] Run full backend tests, golden/reference suites, API compatibility tests,
  OpenSpec strict validation, file-size-policy validation, and `git diff --check`.
- [x] Write a validation report that records migrated authorities, retained
  legacy paths, numerical-equivalence evidence, and rollback points.
