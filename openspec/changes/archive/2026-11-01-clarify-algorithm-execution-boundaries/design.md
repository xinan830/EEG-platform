## Context

See `proposal.md` for motivation and `specs/analysis/*/spec.md` for the target contracts. The current system already has one official-algorithm registry/runtime and typed scalar, series, structured, and structured-series result models. Signal access is less uniform: spectral loads, continuous FAA pair reads, montage-backed sample readers, and dynamic window access pass through different service paths. The design must preserve those scientific differences while making their inputs and evidence explicit.

The cleanup is intentionally limited to current official algorithm execution. It does not remove the general recording viewer, report, event, or acquisition APIs. It removes obsolete compatibility only where the current official algorithm path still emits, derives, or parses a retired Run/result contract. The latest application has no old Run/artifact reader, converter, or format-specific fallback. This change neither accesses nor deletes stored historical bytes; their physical retention is not an application compatibility guarantee.

## Goals / Non-Goals

**Goals:**

- Give every module one documented typed signal-access contract for raw and montage-derived signals, filters, windows, units, missing samples, and evidence.
- Make the selected montage and preprocessing identity travel with the exact signal source used by static, dynamic, on-demand, and combined execution.
- Make typed module results the only source of output kind and algorithm-specific evidence; keep shared serializers algorithm-agnostic.
- Remove reachable legacy result/state adapters in a coordinated backend and WPF update, with no data migration or false promise of old-result compatibility.
- Provide a per-algorithm, per-strategy verification matrix that proves structural cleanup did not change current science.

**Non-Goals:**

- Unify scientific formulas, filter scopes, quality thresholds, frequency bands, window sizes, or channel-role requirements across algorithms.
- Change current scientific versions, computation outputs, UI workflows, or the already accepted montage-selection behavior.
- Add user-defined/plugin algorithms or create a new execution strategy.
- Delete recordings, Runs, artifacts, or raw EEG files.

## Decisions

1. **Model signal acquisition as a typed runtime capability, not a shared scientific algorithm.** Define a small signal-source/preprocessing contract that exposes source roles, selected derived outputs, sample coordinates, V/float64 arrays, filter settings, missing-sample behavior, and provenance. The runtime/service adapter resolves sources and applies declared preprocessing. Modules state what they need. FAA may request a paired input with continuous filtering; Brainbeat may request its own role pair/window semantics; those remain module declarations and algorithm-owned calculations. A single universal `load_spectrum` method was considered and rejected because it would incorrectly make non-spectral algorithms depend on PSD semantics.

2. **Keep result structure in typed module results and shared serialization in a generic adapter.** Use the existing result families where sufficient; add a typed family only when a real module output cannot be expressed clearly. Modules identify output kind, axis/array units, channel-role order, and evidence. The generic boundary validates these declarations, persists arrays as artifacts, and writes common provenance. Moving all serialization into every algorithm package was rejected because it would duplicate artifact and API contracts.

3. **Remove compatibility by reachability and contract inventory, not by deleting anything named `legacy`.** First trace each candidate field or fallback from API input to persistence and desktop consumer. Remove it only when it serves the retired official result contract, update backend and WPF in the same change, and version the current result contract. Preserve unrelated viewer/API fields and raw data. This avoids accidental removal of an active feature merely because a comment uses the word “legacy.”

4. **Do not implement old-result handling.** The new application reads and presents only the current contract. It does not inspect, convert, re-compute, or present a special unavailable state for retired result formats. Stored bytes are outside this code change and are not touched. A code rollback is possible because the cleanup does not rewrite persistent rows or artifacts, but the supported direction is roll-forward; older clients/results have no compatibility guarantee.

5. **Establish the baseline before changing boundaries.** For all nine algorithms, record input roles, montage projection, preprocessing scope, filtering/notch, ranges/sample rounding, output kind/units, quality/failure states, artifacts, and provenance under every declared strategy. Compare current results against existing independent references or frozen fixtures. Structural migration must retain exact equality for discrete/provenance fields and use each output's declared numeric tolerance for arrays/scalars.

6. **Treat size-policy work as part of each touched file's review.** Keep new contract/coordinator modules focused and below the 400-line soft target. For existing oversized files, extract only the responsibility moved by this change and update `docs/code-size-policy.json`; do not mechanically split unrelated state owners.

### Acceptance fix: shared official montage candidates

Manual acceptance found that algorithm registration loaded only persisted user montages, while review also received generated official presets. The shared recording catalogue now generates official ANT presets from the recording's saved channel configuration before merging current profiles. Recorded mappings take precedence over current device mappings for the same preset identity. All candidates retain the existing compatibility checks; the backend still validates and persists independent immutable analysis snapshots. This restores the intended montage-selection behavior without changing scientific formulas or raw data.

## Risks / Trade-offs

- [Risk] Consolidation may hide algorithm-specific filtering differences or shift samples at boundaries. → Keep preprocessing scope declared per module; assert exact sample coordinates and filter provenance, and compare frozen numeric outputs for all affected algorithms.
- [Risk] Removing duplicate fields may break a WPF reader or persisted-result preview. → Search all backend and desktop consumers, update both ends in one change, and add current-contract integration tests plus explicit unsupported-old-contract tests.
- [Risk] A typed common input surface can become a new “god interface.” → Expose only reusable data contracts and bounded source operations; avoid generic algorithm-ID switches and retain narrower capabilities where signal access is genuinely different.
- [Risk] Old runs may not render after the upgrade. → Do not add old-format detection or readers; document that only the current result contract is supported. This change does not operate on stored historical bytes.
- [Risk] Current baseline coverage may not span every declared strategy. → Mark unsupported/unvalidated capabilities explicitly; do not mark a migration task complete without a matched fixture/reference or recorded reason and bounded follow-up.

## Migration Plan

1. Inventory current call sites, result readers, persisted shapes, and compatibility branches; build the nine-algorithm strategy/input/output matrix without changing behavior.
2. Freeze or select current baselines and add contract tests for units, montage identity, sample bounds, filters, missing data, states, quality, and provenance.
3. Implement the typed signal boundary and migrate modules in small groups while running the same baselines after each group.
4. Move output declarations to typed module contracts and converge generic validation/artifact/provenance serialization.
5. Remove only proven-dead official-result compatibility fields and fallback parsers; update WPF request/result consumers and increment the relevant result-contract version. Do not add a compatibility reader, converter, or special old-result presentation.
6. Run focused backend and desktop suites, all-algorithm strategy matrix, independent numeric references, OpenSpec strict validation, file-size policy, and `git diff --check`; update architecture/onboarding docs and tasks as each stage completes.
7. Commit and push only after the tasks are complete and desktop acceptance confirms current-result rendering and ordinary structured failure presentation. Do not introduce a historical-format-specific presentation path.

No database migration or data cleanup is planned. If implementation discovers that a candidate “legacy” field is still required by a current supported workflow, keep it out of the removal scope and record that dependency instead of silently breaking the workflow. Unrelated historical bytes are not an application feature or compatibility path.

## Open Questions

None that block planning. The baseline inventory task will determine whether each current strategy has a usable frozen fixture or needs a new one before refactoring begins.
