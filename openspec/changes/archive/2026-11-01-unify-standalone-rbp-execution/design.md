## Context

See proposal.md. `RbpAlgorithm` is already registered in `build_builtin_modules`, dispatched through `AlgorithmRuntime`, and uses `controlled_frames` for full dynamic execution. `RbpWindowCapability` uses `plan_policy_window`; application wiring gives `PersistentRunQueue` and on-demand workers the same `ScientificComputeCoordinator`. This integration must be reused, not replaced.

Current duplication: runner static calculation, runner dynamic `_execute_multi_frame`, and window `_execute_multi_window` each load spectra and integrate channels independently. An unused `_load_spectrum` is another entry. Full dynamic quality is unconditionally clean, static denominator errors have a different code, and a raised quality gate omits per-channel arrays/evidence in the window adapter.

Baseline: 11 passed, 1 failed. The existing multi-channel static test fails at the common result validator because RBP declares one fixed source role despite accepting multiple channels. Add explicit `source_shape=channel_set` to the public signal declaration; only that declaration permits variable cardinality, while result order, duplicate rejection and named channel array dimensions remain strict. Current desktop consumers do not deserialize or branch on `source_shape`; they use requested channels and the existing result arrays. Fixed-role algorithms retain their existing count checks.

## Goals / Non-Goals

- One typed RBP frame outcome, consumed by static/full/on-demand envelopes; no new queue, group graph or dependency.
- Preserve source/analysis reference, continuous filtering, 50 Hz notch Q=30, five bands, V²/Hz input and ratio output. Revise artifact handling to annotated per-channel science v4. Sample bounds use the public round-to-sample policy; returned seconds stay recording-absolute.
- Do not migrate TNK, display controls, recording access, live acquisition or other algorithms. This does not promise window-only preprocessing or a speedup.

## Decisions

### Approved quality-policy revision

The screenshot and source inspection exposed a batch quality gate: peak/flatline/clipping checks across all channels share a clean-segment mask. The user selected retaining computable values with artifact warnings. Add an explicit typed `per_channel_annotation` spectrum policy, selected only by standalone RBP, and evaluate each channel independently after one shared data preparation. Preserve the strict default for all other algorithms. Artifact-bearing finite segments participate in RBP Welch; expose peak, thresholds, reasons and channel status. Any non-finite/missing samples reject that channel window; do not impute. Zero/invalid band denominator remains unavailable. Use existing partial aggregate quality plus separate channel warnings, never disguise warnings as failures or clean science.

RBP science becomes `official-rbp-v4`, implementation `rbp-runtime-v5-channel-quality`, policy `rbp-dynamic-window-v3-channel-quality`, Definition 2.0.4. Update snapshot, manifest, backend tests and WPF current consumers together; no retired-execution fallback. Show a clear warning explanation in the RBP panel and per-channel warning counts/reasons. Continuous filters and formula stay unchanged. Validate artifact-channel isolation against healthy-channel-only output, non-finite/flatline/zero handling, strategy parity, API serialization and frontend retention of partial results.

File-size assessment: `spectral_analysis.py` already exceeds the 400-line soft target; it owns PSD and STFT use cases. Limit edits to policy routing/response metadata here; channel-quality science goes in a separate scientific primitive. Do not mechanically split the service during this algorithm-scoped change. `algorithm_recording_context.py` is near 400 lines; assess extraction of typed spectral transport separately if additions cross the soft target. Neither file may silently acquire algorithm formulas.

`recordings.py` is also above the soft target (533 lines): this revision adds only the policy parameter forwarding. Assess future extraction along recording catalog versus signal-query use cases, preserving existing registration/provenance. Final sizes: spectral service 562, context 395; no touched file reaches 600 lines.

1. Add `frame.py` for shared input preparation and per-channel computation, returning a typed frame with arrays, channel order, quality, failure, evidence and preprocessing provenance. Keep `official.py` as the unchanged pure numerical function. Reject the alternative of sending a single algorithm through the TNK child graph: it adds no dependency benefit.
2. Runner/window adapters only plan and map that outcome into existing models. Read all selected channels in one spectral request per frame, then run the same formula per channel. Retain one failed-channel result rather than re-reading each channel. Unexpected malformed input propagates as an execution error rather than success.
3. Carry common channel failures/warnings/order, band powers and spectral evidence through each envelope; preserve existing extension keys consumed by WPF. Aggregate dynamic quality as clean only if all frames are clean, otherwise partial. Hard invalid-data rejection remains unavailable; artifacts keep actual measured values with warnings.
4. Public services keep persistence, queues, compute leases, cache, subscriptions and generations. No new resource owner or storage. Science `official-rbp-v4`, implementation `rbp-runtime-v5-channel-quality` and policy `rbp-dynamic-window-v3-channel-quality` identify the revised contract. Update WPF current consumers, warning presentation and fixtures together.

The published RBP Definition digest embeds its implementation identity, so its immutable Definition version advances to 2.0.4. Keep old records unchanged; publish a distinct descriptor instead of weakening digest validation. Run API tests with an isolated `BRAIN_STORAGE_ROOT` so collection cannot use the user's recording database.
5. Test the existing baseline first. Add static/full/on-demand parity for ordered multi-channel success, partial denominator failure and raised/returned quality gates; verify evidence, sample coordinates, one read, cancellation and aggregate quality. Run real-file API RBP tests and RBP combined-strategy tests, existing coordinator tests and strict OpenSpec checks. Frontend production protocols stay unchanged; desktop identity fixtures are checked in the focused desktop suite when available.

## Risks / Trade-offs

- [Scientific preparation can still touch a full recording] → preserve the explicit existing continuous-filter contract; changing boundary science is a separate decision.
- [Richer evidence increases artifact metadata] → store scalar band powers/quality only, never duplicate PSD arrays.
- [Failure normalization invalidates cached envelopes] → advance implementation and policy identities; do not add old readers or fallbacks.
- [Concurrency is not automatic acceleration] → use current shared admission and one batched frame, without per-channel threads.

## Migration Plan

1. Freeze existing tests and add regression cases for parity/failure semantics.
2. Replace the three implementations with the shared frame and remove old private paths.
3. Advance execution identities and update their fixtures.
4. Verify scoped backend/desktop tests and document manual standalone RBP acceptance separately. Rollback uses the prior build; no raw recording, database schema or historical artifact is rewritten.
