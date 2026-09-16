## Why

The current official-algorithm path exposes legacy semantic channel mapping and
algorithm-ID conditionals in generic Run services.  Its Theta/Beta execution
also produces a three-role legacy metric bundle instead of one selectable,
traceable measurement.  This makes the product confusing for users and makes
each new algorithm harder and riskier to add.

## What Changes

- Add a single backend algorithm runtime with a registry, typed algorithm
  contracts, backend-authored parameter schemas, normalized results, and one
  catalog for official and user algorithms.
- Replace the current executable official-algorithm integration with dedicated
  algorithm packages.  Generic services and executors will not branch on a
  concrete algorithm ID.
- **BREAKING** Replace the executable official Theta/Beta contract with
  `official-theta-beta-v2`: one explicit raw recording channel per Run,
  IAPF estimated from that same channel, and one scalar or dynamic series.
- **BREAKING** Remove global `ChannelMapping`, its recording endpoint,
  automatic mapping, mapping UI, and the
  `OFFICIAL_CHANNEL_MAPPING_REQUIRED` failure.  Algorithms requiring multiple
  signals will declare explicit per-Run channel parameters instead.
- **BREAKING** Remove legacy official algorithm facades and legacy executable
  algorithm-entry paths after an import/call-graph audit.  Historical Run and
  Artifact records remain read-only and exportable, but cannot be re-run under
  deleted implementations.
- Keep the frozen `offline-spectral-v3` preprocessing, Welch, units,
  frequency boundaries, quality gates, source-file immutability, viewer
  pipeline, spectrum API, and spectrogram API unchanged.

## Capabilities

### New Capabilities

- `analysis/algorithm-runtime`: Execute registered official and user
  algorithms through one typed, schema-driven Run boundary.

### Modified Capabilities

- `analysis/official-algorithm-execution`: Replace mapped three-role
  Theta/Beta with explicit single-channel Theta/Beta v2 and remove the mapping
  prerequisite.
- `analysis/official-algorithm-catalog`: Return backend-authored parameter and
  result schemas, and expose catalog entries through the unified catalog.
- `data/recordings`: Remove persisted global semantic channel mapping while
  retaining imported raw channel labels and their source order.
- `analysis/run-provenance`: Persist algorithm schema/version identity and
  explicit per-Run input choices for runtime-executed algorithms.

## Impact

Backend changes affect `eeg_core/official_algorithms`, `services/runs.py`,
`services/run_analysis_executor.py`, recording models/migrations, API routers,
and Run request validation.  Frontend changes affect recording types and the
waveform-algorithm workspace, which will render selected algorithms from
backend schemas instead of fixed official-ID checks.  SQLite mapping columns
and endpoints are removed only after migration and historical-read tests pass.
No new external dependency or arbitrary Python plugin capability is introduced.
