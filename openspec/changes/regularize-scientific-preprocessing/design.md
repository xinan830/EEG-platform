# Design

## Context

See proposal.md. Keep the local workstation's current raw-reader projection order, continuous block filtering, finite-context filtering, manual bad-segment validation and ICA fit fingerprint semantics. Preserve existing uncommitted Review fixes.

## Goals / Non-Goals

Goals: one production offline facade, one effective spec per execution, explicit cache publication ownership, scientific helper dependencies pointing downward, and catalogue-driven desktop capability decisions.
Non-goals: new science, changed filter coefficients/order, changed EEG units or bounds, new display filters, a generic pipeline framework, or merging caches with different stored objects. Existing unrelated architecture test failures are measured separately.

## Decisions

- Continuous caches detach in-flight futures on invalidation and use future identity as the publication token. Waiters already attached may finish; subsequent callers create a new generation. Freeze arrays before returning either cached or detached results. ICA cache uses recording generation tokens for delayed model writes, preserving bounded LRU storage.
- Keep OfflineScientificPreprocessingService as the production preparation facade. Move bounded execution/cache ownership to its own existing-concept service only where needed to keep each owner cohesive and under size limits. ContinuousPreprocessingService consumes a validated OfflinePreprocessingSpec, not competing scalar/spec inputs. Preserve requested-channel and resource-budget behavior.
- Move detrend_and_filter_for_inspection unchanged to scientific primitives and export through the scientific package facade. Keep continuous and bounded filters distinct because their sample contexts differ; prove helper equivalence rather than unifying different mathematics.
- Use a preprocessing-owned access dependency holding only access/provenance callbacks and the ICA cache. RecordingService delegates compatibility APIs and lifecycle but no longer chooses preparation engines or repeats preparation configuration. Avoid a new universal manager.
- Add artifact-removal support to existing algorithm signal metadata. Review reads catalogue metadata instead of an algorithm-ID exclusion list; backend remains authoritative and rejects unsupported operations. Retain one frontend preprocessing state owner.
- Reuse existing request-to-spec conversion; centralize effective spec construction and result-identity context enrichment at the appropriate existing boundaries.

## Risks / Trade-offs

- Read-only arrays can expose callers that mutate shared input: verify production and test consumers; make explicit copies only where mutation is intended.
- Detached work may finish for existing consumers: its resources stay owned until consumers release; invalidation prevents future reuse, not unsafe forced mmap closure.
- Metadata additions require coordinated desktop contracts and backend tests. No fallback that falsely claims unsupported removal is allowed.
- Existing API filter and policy defaults, sample-major V/float64, NaN masking, channel projection and numerical behavior remain fixed and regression-tested.

## Migration Plan

Add failing lifecycle tests first, implement cache and immutable publication, then migrate facade/spec/helper dependencies, then metadata consumers. Run focused and broader backend tests, desktop build and affected tests, OpenSpec strict validation, source-size checks and whitespace checks. Record evidence in docs/verification and align architecture pages. Roll back these coordinated internal/DTO edits together; no database or raw-file migration.
