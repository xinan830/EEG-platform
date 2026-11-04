# PSD Frequency Topomap Validation

Change: `add-frequency-resolved-psd-topomap`

The implementation is present in commit `6496eda` (`Complete PSD and topomap analysis updates`). The user confirmed that the full task list, including representative-channel performance checks and interactive Review acceptance, had already been completed. This recheck records the automated checks run while preparing the change for archive.

## Rechecked

- Backend `tests/test_topomap.py`: 19 passed.
- Backend `tests/test_official_algorithm_runs.py`: 28 passed.
- Desktop Review PSD, topomap rendering, and Review binding tests: 19 passed; project and test assembly built successfully.
- `openspec validate add-frequency-resolved-psd-topomap --strict`: passed.
- `git diff --check`: passed.

## Separate in-progress change

A combined backend run also included `tests/test_psd_frequency_contract.py` and produced 51 passed, 1 failed. The failing assertion expects the configured PSD display filter edges to be sent to the loader. The active, uncommitted `standardize-analysis-spectral-contract` work changes those loader arguments to the shared 1–50 Hz analysis preprocessing range. This failure is in that separate in-progress contract transition; the topomap and official-run suites pass independently.

The representative 19/32-channel performance measurements and interactive Review playback acceptance were not repeated during this archive preparation; their completion is recorded from the user's confirmation.

## Spec sync

The three delta specs were merged into openspec/specs/analysis/eeg-topomap, openspec/specs/analysis/official-algorithm-catalog, and openspec/specs/desktop/review-algorithm-link. Main-spec validation passed: 83/83.
