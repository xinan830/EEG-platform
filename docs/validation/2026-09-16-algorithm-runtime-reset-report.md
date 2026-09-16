# Algorithm Runtime Reset Validation Report

Date: 2026-09-16

## Scope

`replace-algorithm-runtime-and-theta-beta-v2` replaces active legacy
algorithm entry points with the typed runtime, removes active recording-wide
channel mapping, and makes the algorithm workspace use backend-authored
catalog metadata. It does not change the frozen `offline-spectral-v3`
preprocessing, Welch parameters, units, IAPF fitting, or quality gates.

## Evidence

- Backend suite: `223 passed`.
- Frontend Vitest suite: `106 passed`.
- Production frontend build: passed (`vue-tsc --noEmit && vite build`).
- Change validation: `openspec validate replace-algorithm-runtime-and-theta-beta-v2 --strict --no-interactive` passed.
- `git diff --check` passed.

## Regression Boundaries

- `backend/tests/fixtures/algorithm_runtime_baseline.json` contains only
  deterministic synthetic-signal identities and scalar golden values; no EEG
  recording or identifying data is stored.
- Architecture tests prohibit product startup and Run execution from importing
  retired official-definition or offline-analysis paths.
- Historical pre-Run `analyses` records remain read-only through the
  compatibility read endpoint, using the application recording database.
- Dynamic Run points retain `null` for unavailable/gate-failed values and
  preserve backend-returned reasons; no browser scientific calculation was
  introduced.

## Known Non-Blocking Warning

Vite reports a production JavaScript chunk above 500 kB after minification.
This is a performance optimization opportunity, not a test or correctness
failure; no code-splitting change is included in this algorithm-runtime
refactor.
