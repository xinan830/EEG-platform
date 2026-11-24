## Why

Official algorithm groups currently encode dependency order, update cadence, input preparation, state progression, and result aggregation in group-specific loops. TNK has a large coordinator whose fixed child order hides the actual graph and makes safe parallel execution, failure isolation, and review difficult. The platform already shares bounded compute admission between full Runs and interactive windows; the missing architecture is an explicit, reusable scheduler for nodes inside a composite algorithm.

## What Changes

- Add a closed, typed dependency-graph contract for official algorithm groups. Each node declares its registered algorithm identity, dependencies, channel roles, input representation, window and cadence policy, state ownership, preprocessing identity, output unit, and whether independent execution is safe.
- Add deterministic graph validation and bounded ready-node scheduling. Independent nodes may run concurrently only after their inputs are prepared and only within a fixed worker budget; each stateful node remains single-writer and frames remain ordered.
- Migrate TNK's 16 internal child adapters from the implicit `_CHILD_ORDER` loop to a versioned graph. Preserve the public `tnk_biofeedback` catalog entry, current formulas, current windows/cadences, role mapping, preprocessing, output units, and per-child failure evidence.
- Separate TNK input preparation, child graph execution, and group result aggregation into responsibility-sized modules; remove the old imperative execution path after migration.
- Publish a TNK frame only after every due or not-due node has a terminal child result. Keep independent child results when another child is unavailable; block only nodes whose required dependency failed.
- Include the graph contract in implementation/cache/checkpoint identity so results from the prior coordinator implementation are not reused. Keep `ViewGeneration` and other display request state out of scientific identity.

## Capabilities

### New Capabilities

- `analysis/algorithm-group-execution`: Versioned, validated dependency graphs and deterministic execution/publication semantics for official composite algorithms.

### Modified Capabilities

- None. Existing platform Run/window lifecycles, scientific result contracts, and API shapes remain the owning contracts; this change supplies the missing group-internal execution capability.

## Impact

- Backend algorithm runtime: add graph declarations and a bounded dependency scheduler.
- Backend TNK modules: declare the TNK child graph and split preparation, scheduling, and aggregation from `group.py`.
- Tests: graph validation, node scheduling/cadence, failure isolation, stable output ordering, and full/on-demand parity.
- No database migration, HTTP/WebSocket shape change, new dependency, formula change, signal preprocessing change, or WPF scientific calculation.

## Compatibility, Scientific Contract, and Migration

- Keep the existing single public TNK group ID. Internal children remain closed-registry implementation nodes and do not become separately selectable algorithms.
- Preserve recording-relative seconds at public result boundaries and half-open `[start_sample, end_sample)` ranges internally. Inputs remain sample-major `V/float64`; role order remains explicitly `Fz, Pz, Oz, F3, F4`; each metric retains its declared output unit.
- Keep the 4 s / 1 s TNK group frame grid, 30 s / 5 s IAPF/HAI/aperiodic cadence, legacy streaming warm-up behavior, bounded TNK spectral preprocessing, quality thresholds, and current per-child failure meanings.
- Do not claim clinical validation or numerical equivalence beyond the automated reference/parity evidence. The scientific version stays unchanged only if all current numerical/window contracts remain unchanged; the implementation and graph identities advance to invalidate old caches and checkpoints.
- Migration is atomic at the registered TNK group boundary: validate the graph before registration, switch the group executor, remove the fixed-order execution loop, and reject invalid graphs rather than falling back to that loop. Stored recordings and artifacts are not rewritten.
