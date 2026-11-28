# Verification — 2026-11-28

## Completed behavior
- Official Run and on-demand IAPF requests now normalize omitted dynamic settings to 20 s / 2 s. Explicit supported values survive; null, zero and negative explicit steps still fail validation.
- Actual 30 s Run output is six windows at the default cadence; explicit 1 s produces eleven. Persisted configuration, execution snapshots and Welch evidence agree. Omitted/default 2 s configurations reuse the same cache identity; explicit 1 s uses a different identity.
- Live coordinator tests verify 20 s warm-up and actual 2 s/default versus 1 s/explicit sample ranges from a nonzero sample origin.
- Real file API fixtures compare full Run and on-demand results, including omitted IAPF settings, source gaps and quality rejection. These are generated fixtures, not the user's real EEG recording.
- Both desktop IAPF charts use fixed 8–13 Hz axes; 7.5 Hz values remain unchanged and numerically available. The backend still searches 7–13 Hz. Peak/COG separation and quality gates remain unchanged.

## Executed checks
- Before fix: targeted new configuration/Run regressions reproduced five failures caused by omitted IAPF settings.
- Backend: `uv run --frozen python -m pytest tests/test_official_algorithm_run_config.py tests/test_official_algorithm_runs.py tests/test_algorithms_catalog_api.py tests/test_iapf_estimator.py tests/test_iapf_algorithm_module.py tests/test_live_scalar_algorithms.py tests/test_live_analysis_coordinator.py tests/test_scalar_on_demand_capabilities.py tests/test_on_demand_scalar_api.py tests/test_on_demand_registry.py tests/test_official_algorithm_independent_references.py tests/test_official_algorithm_numeric_reference_audit.py tests/test_algorithm_presets.py tests/test_run_queue.py -q --tb=short` — 164 passed, 2 dependency deprecation warnings, 53.09 s.
- Backend gate: `uv run --frozen python -m pytest -m gate -q` — 60 passed, 1873 deselected, 2 dependency deprecation warnings, 11.64 s.
- Desktop Release: scalar/IAPF/workspace/registration/parameter filter — 50 passed, 0 failed/skipped, 3 s. Build emitted three existing analyzer/test warnings.
- `uv run --frozen python scripts/check_file_sizes.py` and `git diff --check` passed. No size exceptions added.
- Strict OpenSpec validation passed for this change and `generalize-live-analysis-coordination`. The synchronized main execution spec passes normal validation; strict mode reports four pre-existing over-500-character requirements, verified identical to HEAD. No new long requirement was introduced.

## Limits
No full repository regression, real EEG comparison, desktop manual acceptance, hardware run or CPU benchmark. Tests establish parameter/scheduling/persistence/display correctness, not guaranteed smoothness or clinical validity. Existing unrelated worktree edits are preserved. No commit or push performed.
