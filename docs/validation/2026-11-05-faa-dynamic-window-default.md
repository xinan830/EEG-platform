# Official FAA Dynamic Window Default Validation

Date: 2026-11-05

## Change

Dynamic Official FAA requests now default to the manifest's 20-second window
when `dynamic_window_s` is omitted. Explicit positive overrides and other
algorithms' defaults are preserved.

## Validation

- `backend/tests/test_official_algorithm_run_config.py`:
  `backend/tests/test_faa_on_demand_capability.py` and
  `backend/tests/test_faa_continuous.py`: 12 passed.
- `openspec validate align-faa-dynamic-default --strict`: passed.
- `openspec validate --specs`: 83 passed, 0 failed.
- `git diff --check`: passed.
- Full backend suite: 57 failed, 1323 passed, 1 skipped; failures include
  unrelated frozen result baselines and existing architecture/runtime tests.
- Full desktop suite: 16 failed, 1172 passed; failures include existing Review
  behavior and output-contract tests.
- Manual WPF playback acceptance was confirmed complete by the user.
- Strict OpenSpec validation and `git diff --check` are run during archive.

These results establish the focused request-default behavior; they do not
establish a clean full-suite result.
