# Proposal

## Why

Offline preprocessing has a sound scientific core but callers still select execution paths, reconstruct specifications and own parts of cache cleanup. Continuous prepared samples remain mutable and in-flight preparation can repopulate an invalidated recording cache. These are foundation concerns before further scientific features.

## What Changes

- Publish read-only prepared samples and prevent invalidated continuous/ICA work from restoring cache entries.
- Route production offline preparation through the existing facade; consume one validated effective specification in the execution layer.
- Move shared inspection filtering into the scientific primitives package without changing its mathematics.
- Separate preprocessing lifetime and recording-access dependencies from recording catalogue responsibilities.
- Declare supported preprocessing operations in algorithm metadata and use that declaration in Review.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `signal/preprocessing`: deterministic execution ownership, immutable published samples and invalidation semantics.
- `analysis/algorithm-runtime`: explicit preprocessing capabilities for consumers.

## Impact

Backend preparation, cache ownership, algorithm catalogue metadata, Review DTOs and capability selection, associated tests and architecture documentation. Existing raw files, time/sample bounds, V/float64, ordered channels, NaN semantics, filter coefficients/order and scientific formulas remain unchanged. Catalogue metadata is additive and updated with its desktop consumer; no database migration or new dependency. Roll back coordinated code/DTO changes together; derived in-process caches may be rebuilt. Live execution and display filtering remain separate.
