# Design

## Context
The existing full montage editor already provides draft references, colors and ordering. Ordinary acquisition/review montage selections are shared with algorithms. The earlier append-only bipolar design is superseded.

## Goals / Non-Goals
**Goals:** Reuse the full editor, return an entire named profile in memory, automatically replace the waveform display, and support switching back.
**Non-Goals:** Persistence, algorithm-input changes, raw-recording changes, new scientific calculation.

## Decisions
- Reuse MontageDetailView and draft validation with a session-only save mode and fixed source snapshot. Saving returns a full MontageProfile without store access or device refresh; cancel does nothing. Reject a separate simplified editor because it omits required controls and duplicates UI.
- Keep one replaceable temporary profile per waveform session. Selecting it does not mutate shared algorithm selection. Ordinary profile selection keeps its current semantics.
- Project the full selected temporary profile instead of appending outputs. Reuse existing reference math/filtering, V units, sample counters and gaps; retain output order/colors.
- Include full temporary identity in review display caches and pending-request fences. Bypass persistent display publication while temporary selection is active, retaining raw caches and algorithm selection/revision.
- Clear temporary state on acquisition stop/new session and review disposal. Validate against actual sources before activation.

## Risks / Trade-offs
- [Shared selection leak] → Keep temporary selection at waveform boundary; test algorithm snapshots and raw writer metadata.
- [Stale queued/cached frames] → Full fingerprint and existing request revision fencing.
- [Editor persists or loads unrelated sources] → Fixed snapshot and return-only save; dedicated tests.

## Migration Plan
Replace the unshipped single-trace path and tests in place. No data migration; preserve unrelated working-tree edits.

## Size Review
Review touched files against docs/code-size-policy.json and update responsibility/split assessments where required.

## Validation
Focused tests cover full editor output, ordering/references/colors, selection/switch-back, algorithm/raw isolation, cache invalidation and cleanup. Build WPF/BAML, validate OpenSpec strictly and check diff whitespace. Report hardware/UI checks separately.
