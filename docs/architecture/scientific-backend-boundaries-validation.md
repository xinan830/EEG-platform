# Scientific Backend Boundaries Migration Validation

## Scope

This report records the structural migration state for
`restructure-scientific-backend-boundaries`. It is an engineering migration
record, not clinical validation.

## Verified in this migration

- `app.bootstrap.build_builtin_registry` is the single production composition
  root for concrete official modules.
- `app.algorithm_runtime` imports no concrete module under `app.algorithms`.
- Active Welch PSD, band power, STFT, FAA, IAPF, RBP, and Theta/Beta authorities
  are declared in `app/scientific/authority_manifest.json`.
- Legacy `eeg_core` scientific imports are compatibility/reference paths; the
  migrated implementations are the active authorities documented in the
  ownership inventory.
- Definition, Run, Validation, and algorithm-preset repositories are owned by
  `app.persistence.repositories`; redundant service repository facades were
  removed after their callers migrated.
- The public catalog keeps user-defined definitions visible as retired with
  `status=retired`, `executable=false`, `creatable=false`, and
  `editable=false`.
- Public creation of a new `definition_metric` Run returns HTTP 410 with the
  stable `USER_DEFINED_ALGORITHM_RETIRED` error code.
- Historical user-defined Runs, result summaries, and NPZ artifacts remain
  readable through the existing Run endpoints without starting a new user
  executor. This is covered by the historical-read API regression test.
- The retired metric runner, Runtime adapter, and compatibility execution flag
  were removed. Historical Run and artifact readers do not import executable
  user-definition code.
- The retired definition graph engine is owned by
  `app/legacy/definition_engine.py`; the old `eeg_core` import path remains a
  compatibility facade, and service modules no longer import `eeg_core`
  directly.
- Services now consume the public `scientific.primitives` and
  `scientific.quality` package facades instead of concrete spectral modules;
  this keeps the import boundary stable while typed dependency injection is
  considered separately.
- Deterministic frozen golden values now cover the migrated spectral authority
  and official IAPF, RBP, FAA, and Theta/Beta modules. These fixtures prove
  regression against the declared synthetic inputs; they are not independent
  reference implementations.
- Independent NumPy reference tests now cover the official IAPF, RBP,
  Theta/Beta, and FAA formulas, including the short-window FAA unavailable
  state. The spectral reference endpoint remains the independent evidence for
  preprocessing/PSD behavior, and the STFT primitive now has an independent
  Hann/FFT axis-and-value comparison.
- The retired executor removal is tracked by
  `remove-retired-user-definition-executor`; only scalar Definition validation
  and preview remain available, without reading EEG samples.
- The versioned baseline now includes compact array evidence: exact shape and
  SHA-256 checksums for PSD/frequency and STFT axes/power, plus explicit scalar
  golden values. Comparison tests reject shifted coordinates and tolerance
  violations instead of silently accepting them.

## Compatibility and rollback points

The following adapters remain intentionally present and are not active
scientific authorities:

- `app.eeg_core.spectral`
- `app.eeg_core.official_algorithms.*`
- `app.legacy.playback_processor`

Redundant `services/*_repository`, `eeg_core/analysis_contract`,
`eeg_core/quality`, `eeg_core/official_definitions`,
`eeg_core/official_algorithm_shadows`, and
`eeg_core/official_algorithms/registry` entry points were removed after their
callers moved to canonical owners. Remaining adapters have validation,
historical, or playback callers and need a separate parity gate before removal.

## Verification commands and results

From `backend`:

```text
uv run pytest -q
287 passed, 2 dependency deprecation warnings

Environment: project lockfile (`numpy 2.5.3`, `scipy 1.18.1`, `mne 1.12.1`).
The system Python environment is not an equivalent validation environment.

uv run python scripts/check_file_sizes.py
passed (with registered soft-limit warnings)

openspec validate --all --strict --no-interactive
40 passed, 0 failed

git diff --check
passed (CRLF normalization warnings only)
```

## Explicitly not yet claimed

- The baseline intentionally stores checksums rather than large numeric arrays;
  tests regenerate the deterministic arrays and verify their exact bytes.
- Frozen fixtures and independent-reference tests cover migrated capabilities;
  they demonstrate regression parity on the declared synthetic data, not
  clinical validity or physical-device timing.
- The typed contract types now exist under `scientific/contracts/types.py`, but
  their adoption across every primitive and official module is still pending;
  this migration is not claiming that all existing payloads have already been
  converted to those types.
- Compatibility facades remain until all active callers are migrated and a
  later removal change explicitly approves deletion.

## Remaining acceptance gates

The legacy streaming biofeedback processor still powers `/api/playback` and
cannot be removed until a versioned streaming replacement has parity evidence
for filter state, update cadence, IAPF locking, RBP, and derived metric payloads.
Physical ANT/eego lifecycle verification is tracked separately.
