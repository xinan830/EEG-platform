# Retirement cleanup validation

Date: 2026-10-21. Starting commit: d1c09a3. Scope: engineering verification, not clinical validation.

## Completed changes

- Previous 27/27 management change archived with its validation handoff; three current specs synchronized. An existing runtime delta classification error was corrected before archival.
- Removed historical shadows, realtime helper functions, their CLI and dedicated tests; current metadata assertions moved to current tests.
- Current Definition graph/parameter validation has its own domain owner. Removed unused graph execution and scalar formula interpretation without forwarding shims.
- Current Brainbeat formula moved unchanged into its own module. Removed the unused realtime descriptor, old bundled three-channel metrics, unused FAA historical limits and catalog manifest alias.
- Backend indexed results transport persisted per-window provenance. Both WPF Full Run preview paths reject missing/incorrect STFT scientific evidence with `FULL_WINDOW_CONTRACT_UNAVAILABLE`, preserve the sample range and return no numeric result.
- Removed old cached-window provider signature adaptation; the cancellation test wrapper now forwards the current cancellation callback.
- Updated current documentation and marked old migration/audit reports as historical records. Source data and database migrations were not changed.

## Verification

| Check | Result |
| --- | --- |
| Backend full suite | 845 passed, 7 warnings, 234.34 s |
| Final shared cancellation/provider change: coordinator, official Runs, retirement guard, size-policy tests | 35 passed |
| Final cancellation wrapper fix: offline reuse, STFT reuse, isolation, offline execution, Run controls | 30 passed |
| WPF full suite and compilation | 853 passed, 0 failed, 0 skipped |
| WPF Full Run focused suite | 32 passed |
| Current scientific tests after bundled-metric removal | 73 passed |
| Independent numerical audit, nine-algorithm strategy matrix and new definition/retirement checks | 24 passed |
| Cleanup OpenSpec strict validation | passed |
| Main specs strict validation after baseline archive | 58 passed |
| Source size policy | passed after reviewing three stale/missing existing registrations |
| git diff --check | passed |
| Retired imports in app/scripts/tools and old WPF compatibility tags | none found |

The full backend suite ran before the final provider-signature simplification;
the two targeted sets above ran after that change and the wrapper fix. WPF
verification exercises both adapter/presenter paths automatically; no separate
manual desktop session was performed in this cleanup.

## Frozen numerical artifacts

All five existing official-algorithm JSON/NPZ baseline files match the starting
SHA-256 values in inventory.md. No expected array or tolerance was regenerated.
The standalone current spectral calculation was also compared with the frozen
analytic source and arrays at 100 Hz, rtol=1e-10 and atol=1e-20:

| Array | Maximum absolute error | Maximum-error coordinate | Failed coordinates |
| --- | --- | --- | --- |
| Welch frequency | 0 | [0] | none |
| Welch PSD | 0 | [0,0] | none |
| STFT time centers | 0 | [0] | none |
| STFT frequency | 0 | [0] | none |
| STFT power | 0 | [0,0,0] | none |

With every delta zero, the coordinate above is simply the first array element,
not a failing point. Algorithm-specific numerical, quality, range, status and
provenance tests remain green. Strategy declarations, including unsupported or
unvalidated reuse/combined claims, are unchanged; cleanup does not promote any
capability to a stronger validation state.

## Environment and limitations

Python 3.13.3, NumPy 2.3.4 and SciPy 1.16.3 were used in an ignored isolated
environment under `.artifacts/retirement-venv`. Other dependencies come from
the existing backend environment. A local ignored `.pth` exposes those packages
to subprocesses; initial child-process dependency failures were resolved and
process-recovery tests passed. The user's normal environment was not repinned.

Existing FastAPI/Starlette deprecation warnings, non-finite scientific boundary
warnings and WPF NuGet compatibility/analyzer warnings remain. They did not
fail these checks. No real EEG, identifying information, DLL/PDB or build cache
is added to the change. Delivery includes only source, tests, documentation and
OpenSpec records; the Git commit records the final delivery identity.
