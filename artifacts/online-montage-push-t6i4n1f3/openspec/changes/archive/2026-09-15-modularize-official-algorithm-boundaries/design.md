## Context

See proposal.md. Official Definition records are durable audit evidence, while their scientific contracts and runtime status are code-owned. The platform is local, single-user, and keeps scientific computation in Python.

## Goals / Non-Goals

**Goals:**

- Make one registry the source of truth for official identity, role requirements, execution category, scientific version, and availability.
- Preserve the old import locations and frozen result contracts while giving each official algorithm an owned module boundary.
- Show official algorithms even while they are intentionally unavailable for ordinary runs.

**Non-Goals:**

- No official executor cutover, no user Python modules, no DAG editor change, no database schema change, and no clinical claim.

## Decisions

- **Code-owned registry, SQLite-backed installation evidence.** The registry defines five supported platform algorithms; `AlgorithmDefinition`/version records confirm that their immutable published definitions are installed. SQLite is not a second source of scientific truth.
- **Shadow-only capability is explicit.** `availability=shadow_validation` and `is_runnable=false` are API data, not inferred from display name or definition graph. This avoids accidentally exposing incomplete composite algorithms.
- **Compatibility facades remain.** Existing imports (`faa`, `offline_metrics`, `realtime_spectral`, `official_definitions`) re-export new implementations. A big-bang rename would needlessly risk callers and golden tests.
- **No implicit channel role substitution.** Fz/Pz/Oz and F3/F4 roles are declared in the registry. In particular, O2 or the third source channel never becomes Oz without an explicit mapping supplied upstream.

## Risks / Trade-offs

- [Registry and installed Definition diverge] → catalog fails closed with `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`; it never advertises a partially installed official algorithm as usable.
- [Refactor changes numerical behavior] → retain frozen existing interfaces, test identity and shadow/golden behavior before and after extraction.
- [Frontend hides user algorithms on catalog failure] → fetch official catalog independently and render a scoped failure notice only for that group.

## Migration Plan

1. Add contract/registry and catalog tests before changing callers.
2. Extract calculation entry points, leave compatibility facades, and run shadow/golden regression.
3. Derive capability metadata and frontend official display from the registry.
4. Deploy as an additive API change. Rollback is safe by reverting frontend catalog use and retaining old imports/routes; no persisted data needs reversal.
