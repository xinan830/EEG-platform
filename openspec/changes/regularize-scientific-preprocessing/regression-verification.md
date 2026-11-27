# Historical backend regression closure — 2026-11-27

## Scope and reproduced failures

The previous report mentioned 69 historical failures. Replaying the saved failures on HEAD `81c1dac` reproduced 67, rather than assuming the old count was still current. Most were stale fixtures or evidence contracts following already-existing scientific upgrades; they were not 67 independent production defects.

- 36 frozen result-boundary cases compared current science and metadata to obsolete v4 snapshots.
- 16 combined/on-demand cases requested retired 5/10-second IAPF or Theta/Beta windows.
- 4 contract-matrix cases assumed an older registry, current-version fingerprint or nine-algorithm coverage.
- 4 registration/source checks assumed ten modules or failed to recognize the parameterized individualized BrainBeat desktop handler.
- 3 PSD frequency cases expected the static output range in dynamic mode.
- The remaining four concerned a fabricated migration fixture, an unowned live coordinator, malformed source admission and an outdated file-reader regression tool.

## Repairs

- Registration checks resolve each parameterized result handler's actual class and algorithm ID, instead of routing every shared handler to the TNK class. This is the only production application change.
- Migration tests create a real schema-7 database via the first seven migrations; production migrations are unchanged.
- The cache-maintenance fixture installs and disposes its live coordinator.
- Corrupted JSON is checked through the actual HTTP admission boundary: explicit 422, no fake ready result, no artifact and no source mutation.
- Test windows, registry lists and dynamic PSD expectations match current declared contracts.
- The independent recording reference now checks the existing standard Theta/Beta bands, classic/individualized BrainBeat formulas and supported IAPF peak/COG diagnostics. The real-file reader test covers 40 scenarios: ten algorithms × REF/AVG × static/dynamic, including replay and tamper detection. Its source is deterministic synthetic data written in the recording format, not a patient's recording.
- Diagnostic tests explicitly own INFO capture. A full run with `BRAIN_LOG_LEVEL=ERROR` initially exposed nine assumptions about host logging state; the 61-test affected suite passes after the fixture correction. Production logging remains unchanged.

## Frozen evidence

Existing `docs/validation` JSON/NPZ files are untouched, including their environments and checksums. The historical numerical/strategy reports continue to describe nine historical algorithms, not today's registry.

Current evidence lives in `backend/tests/fixtures/algorithm-result-boundary-20261127.{json,npz}` and `official-algorithm-contracts-20261127.json`. The current registry contains ten standalone algorithms and the TNK group. The snapshot captures 40 clean/rejected static/dynamic cases with exact metadata, shapes and coordinates, retaining `rtol=1e-10`, `atol=1e-20`. The actual resolved configuration is included and its digest checked.

For Band Ratio, FAA, Peak Frequency, PSD, RBP and STFT, 24 cases additionally compare every numeric artifact array to the original v4 archive. Removing only the verified added `frequency_mode` and null `preprocessing` fields reconstructs each original configuration hash exactly.

The changed IAPF, Theta/Beta and both BrainBeat contracts are checked independently from deterministic raw samples with a separate filter/FFT/integration/closed-form OLS reference before freezing and during replay. Current capture records Python 3.12.0, NumPy 2.5.4 and SciPy 1.18.1. The capture command refuses to overwrite existing evidence.

## Validation

- Independent formula audit: 5 passed.
- Updated real-file replay/reference suites: 14 passed.
- Current boundary, historical array preservation, contract matrix and independent numeric suites: 77 passed.
- Diagnostic suites: 61 passed.
- Historical array/configuration preservation after the final assertion addition: 24 passed.
- Architecture/API/size gate selection: 59 passed.
- File-size policy and strict OpenSpec validation: passed.
- Complete backend suite: **1884 passed, 0 failed, 2 skipped, 7 warnings in 355.06 seconds**.
- Frozen-capture overwrite guard: refused an existing target and left the archive checksum unchanged.

Final command (from `backend`, with isolated `BRAIN_STORAGE_ROOT` and `BRAIN_LOG_LEVEL=ERROR`):

```powershell
.\.venv\Scripts\python.exe -m pytest tests -q --tb=short --durations=15 -o junit_family=legacy --junitxml=.pytest_cache/baseline-repair-full-final.xml
```

The two pre-existing conditional skips are explicit: Windows lacks symbolic-link creation privilege for `test_linked_cache_file_is_refused`; the TNK historical reference test requires NumPy 2.2.6 while this environment has 2.5.4. No new skip/xfail marker was introduced. The seven remaining warnings are two dependency deprecations, one deliberate nonfinite peak-frequency case, two sparse synthetic MNE geometries and two invalid synthetic EDF header dates.

Logs and JUnit are stored under `backend/.pytest_cache/baseline-repair-*`. Test processes use isolated temporary storage. No source EEG files, persistent user database, production scientific formula, filter coefficient, unit, reference order, API/WebSocket payload, desktop source or dependency version is changed.

## Limitations

This is backend engineering regression closure. It does not rerun private EEG acceptance, desktop build/manual interaction or clinical validation. TNK's existing release-validation declarations are retained. Existing dependency deprecation and deliberate synthetic quality warnings are reported, not hidden or converted to successful science.
