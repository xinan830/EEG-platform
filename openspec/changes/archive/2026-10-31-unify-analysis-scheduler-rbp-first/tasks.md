## 1. Contract and Baseline

- [x] 1.1 Inventory all TNK child identities, role order, input representation, windows/cadences, state owners, dependencies, preprocessing identities, units, failure semantics, and current full/on-demand callers.
- [x] 1.2 Capture focused baseline outputs and existing full-versus-replay parity evidence without changing algorithms, source readers, public result shapes, or playback code.
- [x] 1.3 Record implementation/file-size assessment for `group.py` and identify responsibility boundaries before moving code.

## 2. Shared Graph Contract

- [x] 2.1 Add the new algorithm-group-execution capability spec and validate its observable scenarios.
- [x] 2.2 Implement typed graph/node/dependency contracts with stable topological layers and structured validation for duplicates, missing edges, cycles, roles, units, and concurrency declarations.
- [x] 2.3 Add focused graph tests for deterministic layers, invalid references/cycles, required and optional edges, and bounded parallel-safe execution.

## 3. TNK Graph Declaration

- [x] 3.1 Declare all current TNK child nodes from their registered module manifests with explicit channel roles, representations, units, statefulness, preprocessing, window duration, and update cadence.
- [x] 3.2 Declare the optional spatial/stability inputs to composite meditation and optional aperiodic fit context for BrainBeat Flat; verify these edges match actual consumption.
- [x] 3.3 Validate graph completeness against the closed child registry and preserve the one public `tnk_biofeedback` catalog entry.
- [x] 3.4 Add schedule-contract tests for every child, including FAA unsupported dynamic behavior, 30 s / 5 s nodes, 4 s / 1 s nodes, and shared legacy stream warm-up behavior.

## 4. TNK Responsibility Split and Execution

- [x] 4.1 Extract shared source preparation and child-window construction; keep access, quality gates, channel ordering, sample bounds, and caches single-owner and serial.
- [x] 4.2 Extract child execution so it consumes prepared windows and dependency outcomes rather than discovering dependencies by list order.
- [x] 4.3 Run independent parallel-safe nodes through a request-scoped two-worker bound; keep each node's state single-writer and frame replay sequential.
- [x] 4.4 Extract deterministic child-to-metric mapping and group quality/evidence publication; publish only after all nodes are terminal.
- [x] 4.5 Switch full dynamic and on-demand checkpoint replay to the same graph executor, then remove `_CHILD_ORDER` and the old execution loop.
- [x] 4.6 Version graph, implementation, and dynamic checkpoint identity without changing the scientific version unless numerical/window contracts change.
- [x] 4.7 Keep all changed handwritten TNK/runtime production modules at or below the 400-line target where practical; document any exception with a responsibility-based split assessment.

## 5. Verification and Acceptance

- [x] 5.1 Verify child cadence, role mapping, units, structured failure isolation, deterministic output order, and no-due/null semantics with focused tests.
- [x] 5.2 Verify exact full/on-demand target parity and checkpoint replay across forward/backward targets using current numerical tolerances.
- [x] 5.3 Compare against captured pre-refactor TNK numerical outputs on representative deterministic inputs; investigate every difference before retaining the current scientific version.
- [x] 5.4 Run focused backend TNK and graph tests, OpenSpec strict validation, and `git diff --check`; do not run unrelated suites.
- [x] 5.5 Record remaining manual real-recording playback acceptance separately; automated validation does not establish real-time responsiveness or clinical validity.
