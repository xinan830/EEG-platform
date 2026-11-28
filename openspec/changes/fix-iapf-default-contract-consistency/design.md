# Design

## Context
See proposal.md. Run requests normalize defaults before execution and fingerprinting; the desktop already supplies catalog defaults. Shared chart styling owns both display axes.

## Goals / Non-Goals
Complete omitted IAPF dynamic parameters and restore the requested display. Do not change estimator coefficients, 7–13 Hz search, filters, quality gates, channel order, units, or unrelated work in the dirty tree.

## Decisions
- Extend the existing request before-validator with IAPF 20 s / 2 s defaults, using setdefault so explicit supported parameters survive. Keep unrelated algorithm defaults intact. A new generic registry lookup in the API model would add cross-layer coupling and is unnecessary for this correction.
- Keep the existing implementation identities: computations already use 50% Welch and 2 s when explicitly requested. Correct default normalization changes persisted configuration and cache identity; no result rewriting or fallback reader is needed.
- Fix the shared chart minimum to 8 Hz; values below the visible axis remain available numerically. Peak/COG track separation remains unchanged.
- Add Run integration tests for omitted and explicit parameters, comparing output window coordinates, persisted parameters and frozen snapshots. Add a live coordinator test that receives actual scheduled 20 s windows at 2 s/default and 1 s/explicit cadence, including a nonzero sample origin.

## Risks / Trade-offs
- Omitted IAPF parameters now yield fewer results: verify 0–20, 2–22 through 10–30 s; explicit 1 s still yields 11 windows.
- 7–8 Hz estimates are outside the requested plot: keep numeric values unchanged and verify the display-only boundary.
- Window-local live filtering differs from offline filtering; tests prove scheduling and contracts, not universal smoothness or clinical accuracy.

## Migration Plan
No schema migration. Restart backend and desktop to load corrected defaults/style. Existing Runs remain unchanged; callers can explicitly request 1 s. Rollback only these scoped patches and restore the old omitted-request semantics. No new runtime dependencies; all touched business files remain below 400 lines or within existing reviewed caps.

## Verification
Run new regressions first, then backend IAPF/config/catalog/live/on-demand/reference suites, gate tests, desktop scalar/workspace/registration tests, file-size and diff checks, and strict OpenSpec validation.
