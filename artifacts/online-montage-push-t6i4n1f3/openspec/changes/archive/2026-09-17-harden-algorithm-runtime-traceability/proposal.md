## Why

The runtime algorithm packages are now usable, but a review found that a
queued official Run can lose its resolved definition identity, and a future
multi-version registry can resolve one version for provenance while executing
another. Official metadata is also duplicated between executable manifests and
the lifecycle catalog, while generic Run resolution still contains FAA-specific
scientific metadata.

## What Changes

- Persist the resolved Definition ID and Definition version for every queued
  official Run, and install frozen official Definitions before they are needed.
- Pin the exact resolved scientific version from request validation through
  execution and cache identity.
- Make executable module manifests the source of official catalog identity for
  runnable algorithms; retain one explicit non-runnable BrainBeat descriptor.
- Move execution snapshot metadata (window, preprocessing and quality policy)
  behind a module-owned runtime contract so Run services do not branch on
  algorithm IDs.
- Add validated common and algorithm-specific evidence envelopes before a Run
  completes, without changing EEG calculations or historic result readability.

## Impact

This change affects runtime contracts, official manifest composition, Run
resolution, the persistent queue, result serialization, catalog responses and
their tests. It does not change spectral mathematics, units, time windows,
quality decisions, frontend presentation state, or legacy read-only Runs.
