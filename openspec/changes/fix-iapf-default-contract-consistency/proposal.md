# Proposal

## Why
IAPF already declares 20 s windows, 50% Welch overlap and 2 s default updates, but omitted Run API parameters still become 10 s / 1 s. Its chart also drifted from the requested fixed 8–13 Hz display to 7–13 Hz.

## What Changes
- Complete IAPF dynamic request defaults as 20 s / 2 s; preserve explicit supported values, including 1 s.
- Restore live and review IAPF display axes to 8–13 Hz without changing the 7–13 Hz scientific search.
- Reconcile current specifications and verify actual Run and live window coordinates, persisted configuration and spectral evidence.

## Capabilities
### New Capabilities
None.
### Modified Capabilities
- `analysis/official-algorithm-execution`: distinguish IAPF default cadence from Theta/Beta and preserve explicit overrides.
- The same capability documents live/review IAPF presentation; reconcile the existing live-coordination change without adding a duplicate capability.

## Impact
Run request normalization, scalar chart style, tests and specifications. Desktop consumers already send explicit durations. Omitted IAPF dynamic parameters intentionally change; explicit values, static behavior and other algorithms remain unchanged. No dependencies or database migration. Units remain V internally and Hz for IAPF; channel order, quality gates, estimator and Peak/COG separation are unchanged. No claim of clinical validation, guaranteed smoothness or identical live/offline spectra. Existing Runs are not rewritten; normalized parameters remain part of execution/cache identity.
