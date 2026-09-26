## Why

The backend official-algorithm Runtime, Run persistence, artifacts, and
structured-result preview are complete, but the production WPF client cannot
use them. The current Vue application is a validation workbench, not the
product client. A completed WPF recording therefore stops at local raw files
and cannot be registered, analyzed, or reviewed through the official
algorithm pipeline.

## What Changes

- Define a stable local HTTP contract for registering a completed WPF raw
  recording as a backend `Recording` without allowing WPF to write SQLite or
  scientific artifacts.
- Add WPF typed clients for the authoritative official-algorithm catalog,
  Analysis Run creation/status, artifacts, and bounded structured previews.
- Add a WPF algorithm workspace that consumes backend parameter schemas,
  submits static Runs, observes terminal states, and renders backend-returned
  scalar and structured results.
- Preserve the existing WPF acquisition/review state machines and immutable
  raw recording format.

## Non-Goals

- No new scientific algorithm or formula changes.
- No real-time or streaming algorithm Runtime.
- No frontend/Vue feature expansion; Vue remains a validation client.
- No WPF-side FFT, PSD, STFT, quality classification, unit conversion, or
  result fabrication.
- No direct WPF access to backend SQLite, artifact files, or internal Python
  modules.

## Compatibility And Migration

Existing backend Recording/Run APIs remain unchanged. The new recording
registration endpoint accepts the completed WPF manifest and local recording
directory through an explicit local-only contract. Existing imported
recordings continue to use their current path. WPF adoption is additive until
the first vertical slice is validated.

## Scientific And Provenance Impact

The backend remains the sole owner of sample interpretation, units, channel
order, requested/actual ranges, quality states, algorithm versions, and
artifacts. Registration must preserve the WPF manifest identity, sampling
rate, channel table, lifecycle gaps, and source hash. A Run is not submitted
until registration succeeds.

## Risks And Rollback

The main risk is a mismatch between WPF chunk encoding and the backend reader.
Registration must reject incomplete, corrupted, or ambiguous manifests rather
than guessing. Rollback is limited to the WPF algorithm workspace and
registration client; existing acquisition and backend Run data remain valid.
