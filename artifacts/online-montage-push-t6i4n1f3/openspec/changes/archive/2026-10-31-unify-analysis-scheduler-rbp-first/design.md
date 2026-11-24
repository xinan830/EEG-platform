## Context

See `proposal.md` for motivation and scope. `AlgorithmRuntime` already resolves one trusted module, validates its config and result, and attaches sample-coordinate evidence. Full Runs and on-demand windows retain separate persistence and cancellation lifecycles, but application wiring passes both through the same `ScientificComputeCoordinator` for bounded and fair scientific admission. Those boundaries remain in place.

TNK's `TnkBiofeedbackGroupAdapter` currently owns public catalog metadata, channel resolution, spectrum preparation, the child-order constant, per-child cadence, state reset, child execution, metric mapping, quality aggregation, full-frame generation, and evidence. The fixed order also carries two data dependencies: composite meditation consumes the current spatial/stability results, and BrainBeat Flat may consume the current aperiodic fit evidence. TNK window replay checkpoints the child instances and calls the same child loop, so the replacement must preserve ordered state transitions in both full and replay execution.

## Captured Baseline

Before changing the execution path, `group.py` measured 774 lines, beyond the 600-line high-risk threshold; its responsibilities divide into serial frame input preparation, dependency scheduling, and deterministic result publication. The focused baseline command ran 29 graph/TNK tests: 28 passed and `test_brainbeat_flat_fits_its_current_window_before_independent_aperiodic_schedule` failed because the active Python environment lacks the declared `specparam` dependency (`ModuleNotFoundError`), so that test returns `TNK_FIT_INVALID`. `test_dynamic_dispatch_preserves_each_child_per_window_and_metric_units` now records representative pre-refactor values for relaxation, spatial distribution, rhythm stability, composite meditation, BrainBeat, and RBP. The existing on-demand parity test passed and compares every output metric against the corresponding full-run frame exactly, including NaNs.

## Goals / Non-Goals

**Goals:**

- Make algorithm-group dependencies and node execution policy explicit and reusable.
- Validate graph integrity before an algorithm group can be registered or run.
- Prepare shared signal data once per frame, then schedule ready child calculations without racing shared caches or readers.
- Bound child concurrency, keep state local to one node, and publish deterministic metric/evidence ordering regardless of completion order.
- Keep the existing result and window contracts, source/preprocessing semantics, output catalog, and replay checkpoint lifecycle.
- Split TNK's over-600-line orchestration file by responsibility, keeping changed handwritten production modules under the project's 400-line target where practical.

**Non-Goals:**

- Merge durable full-Run persistence with ephemeral on-demand window sessions.
- Replace `ScientificComputeCoordinator`, recording access, preprocessing, result storage, or desktop presentation.
- Change any TNK formula, window duration, cadence, boundary policy, filter, quality threshold, input role, or output unit.
- Expose TNK child nodes as independent catalog entries or add arbitrary user-authored Python graphs.
- Parallelize frames of one stateful node, skip missed state transitions, or claim a general speedup without measurement.

## Decisions

### 1. Use a closed, typed graph

Add a small runtime graph contract containing node ID, declared dependencies, and whether each dependency is required for execution or only supplies ordered context. A node carries its existing registered module identity plus group-owned input roles, representation, window duration, cadence, statefulness, preprocessing identity, output unit, and parallel-safety declaration. Graph construction rejects duplicate IDs, missing references, self-dependencies, cycles, undeclared required dependencies, and invalid concurrency declarations.

The graph is static application metadata, not user configuration. It uses registered module objects and callables already supplied by the application; it introduces no dynamic imports, `eval`, or executable graph persistence. Stable topological layers preserve declaration order within ties.

### 2. Keep platform admission and group scheduling distinct

`PersistentRunQueue` remains responsible for durable Run status/recovery. The on-demand service remains responsible for exact window identity, subscriptions, and cancellation. Both continue acquiring the existing shared compute lease. The new group scheduler runs inside one admitted algorithm computation and cannot bypass the global compute limit. It owns only dependencies and bounded child-node execution within that computation.

### 3. Prepare shared inputs before scheduling calculations

For each TNK frame, reset or advance state exactly as today, prewarm shared spectra, and prepare every due node's typed config, input window, and any structured source-quality failure serially. This keeps recording access and shared-spectrum caches single-owner. Then the graph executor runs prepared node calculations by topological layer. Nodes with `parallel_safe=True` may share a bounded two-worker pool; the pool is local to one group execution request and is closed on completion. The scheduler never performs file reads.

TNK stateful modules remain one mutable instance per node, are called at most once per frame, and frames are still executed in ascending sample/time order. Legacy streaming models remain one shared stateful producer and are advanced once before their child results are mapped. The same executor path is used by full analysis and checkpoint replay.

### 4. Model dependencies without changing current formulas

The graph records `tnk_spatial_distribution -> tnk_composite_meditation` and `tnk_rhythm_stability -> tnk_composite_meditation` as ordered optional dependencies. It records `tnk_aperiodic -> tnk_brainbeat_flat` as an ordered optional context dependency: the Flat node still computes its own current-window fit, and consumes the aperiodic result only when that scheduled result contains usable fit evidence. A missing or failed optional dependency is passed as unavailable context and does not suppress the dependent's independent result.

Other current child calculations have no graph dependency. A required dependency failure produces an explicit child-level dependency failure and does not prevent unrelated ready nodes from running. Expected scientific failures continue to use the module's existing `AlgorithmResult` with `null` values, quality, failure, and evidence; unexpected exceptions are not silently converted into success.

### 5. Separate computation order from publication order

The graph executor returns results keyed by node ID. The TNK aggregation layer maps them through a separately declared stable metric order and role order. Each due child, not-due child, unsupported child, or dependency-blocked child receives one terminal result for the frame. A frame is built only after all declared nodes reach a terminal state, and it continues to use `AlgorithmStructuredSeriesResult` for full analysis or `DynamicWindowResult` for on-demand replay.

### 6. Version graph and implementation identity

Add an explicit graph contract version to TNK execution provenance and the registered group implementation identity. Advance the dynamic window policy identity so old replay checkpoints cannot be restored under the new executor. Preserve the TNK scientific version only if numerical reference comparisons and exact full/on-demand parity pass and no scientific contract changed. Cache keys continue to use the current definition, scientific/configuration, implementation, preprocessing, and sample-window identities. UI request/view generations remain presentation-only.

### 7. Split by responsibility, not line count

Move TNK child declarations and graph construction, prepared input creation, node execution, and result aggregation behind small focused modules. Keep `group.py` as the public group adapter and frame loop. Do not create generic wrappers for responsibilities already owned by `AlgorithmRuntime`, `ScientificComputeCoordinator`, or result models. Update direct tests to the new graph contract; do not retain `_CHILD_ORDER` or a legacy execution fallback.

## Risks / Trade-offs

- [Concurrency changes wall-clock ordering] Independent nodes may finish in a different order → results are collected by node ID and emitted in the fixed metric order; stateful frames remain serial and dependencies gate execution layers.
- [Shared mutable inputs] Concurrent calculations could mutate a shared spectrum or provenance → prepare per-node window/provenance objects before dispatch, keep shared PSD arrays read-only by convention, and test deterministic order and repeatability.
- [Native numerical library oversubscription] Concurrent `specparam`/NumPy work can compete with the process → cap child work at two nodes and retain the outer shared compute lease; measure before raising the cap.
- [Graph metadata drifts from implementation] A missing cadence or role declaration can mis-schedule a child → validate each graph node against its registered child manifest and test every current child ID and schedule.
- [Checkpoint mismatch] Old child state snapshots may not match the new execution identity → graph/implementation identity changes force a fresh replay chain; no compatibility restore is attempted.
- [Scope creep into scientific redesign] Window or preprocessing changes would confound the scheduler refactor → any changed scientific value contract requires a separate OpenSpec change and its own numerical validation.

## Migration Plan

1. Record current invariants and tests; add the graph model and graph validation tests without changing execution.
2. Declare every TNK child node and its cadence, input contract, dependencies, state owner, unit, and parallel policy.
3. Extract serial shared-input preparation and result mapping from the large group adapter.
4. Replace the fixed child loop with graph-layer execution and bounded safe-node concurrency; use the same path for full and on-demand replay.
5. Change graph/implementation/checkpoint identity, remove the old order loop, and verify all TNK result/caching callers still resolve the one public group.
6. Run focused backend tests and strict OpenSpec validation. Real recording playback remains a separate manual acceptance and is not implied by unit-test success.

Rollback is a code release rollback to the prior application build; no database migration or in-place artifact rewrite is introduced. The new implementation identity prevents accidental reuse across versions.

## Open Questions

None. The initial two-node child concurrency cap is fixed for this migration and can be changed only after measurement and parity evidence.
