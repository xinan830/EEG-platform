## Why

Cold-cache playback starts each PSD/RBP pair only after its playback deadline. Across 88 paired-window records in the 2026-11-07 experiment, median source loading was 662.55 ms and median projection was 95.35 ms; table-application records showed actual window lag up to 1.641 s. Formatting is not the primary cause. Waiting for each projection before requesting the next window adds avoidable delay.

## What Changes

- Prepare up to two upcoming offline playback windows and their backend numeric projections, using the existing PSD/RBP adapters and bounded backend scientific resources.
- Publish prepared results only at the matching playback target; retain actual bounds and pending status when computation misses its deadline.
- Invalidate preparation on seek, timeline drag, configuration changes, pause and close. Preserve source identities, ordered channels, units and quality validation.
- Add preparation/publication diagnostics and deterministic cold-path scheduling regressions.
- Extend the same bounded preparation to FAA/IAPF, which the 01:18–01:20 experiment shows still request windows only at their deadline. FAA additionally re-reads and filters the entire recording for every independent window because its prepared pair is owned by the short-lived context.
- Share the existing identity-validated continuous preprocessing owner with FAA, and release terminal subscriptions independently from result delivery.

## Capabilities

### Modified Capabilities
- `desktop/review-spectral-values`: bounded preparation and playback-time publication for both numeric panels.
- `desktop/on-demand-dynamic-review`: bounded FAA/IAPF preparation with cancellation and exact-time adoption.
- `analysis/on-demand-windows`: reusable continuous FAA sources and terminal subscription lifecycle.

## Impact

Desktop Review binding/coordinator, shared preparation queue and subscription observation; backend continuous preprocessing ownership; tests and validation documentation. No public HTTP/WebSocket, scientific formula, stored artifact format, database schema, raw recording or topomap behavior changes. Preparation uses existing artifact persistence and can consume at most two additional future windows per source. No migration; reverting these scheduling/ownership changes restores demand-only behavior without changing historical artifacts.
