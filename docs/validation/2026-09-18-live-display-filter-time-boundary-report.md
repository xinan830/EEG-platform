# Live Display Filter Time-Boundary Validation

Date: 2026-09-18

## Behavior Locked

A live display-filter control change takes effect at the first raw sample after
the action. Displayed values before that counter remain unchanged. The next
Python filter session receives prior contiguous raw data only as non-displayed
causal warm-up. Raw recording chunks remain untouched V/float64 values.

## Automated Verification

- `pytest backend/tests/test_live_filter_service.py -q`: 4 passed.
- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  61 passed.
- `dotnet build desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`:
  succeeded with 0 warnings and 0 errors.
- `openspec validate switch-live-display-filter-at-time-boundary --strict --no-interactive`:
  valid.
- Scoped `git diff --check`: no whitespace errors.

## Residual Constraint

The bounded pre-roll improves causal filter initialization but cannot prove a
very-low high-pass filter is fully settled. This is display-only behavior and
must not be represented as scientific preprocessing or clinical validation.
