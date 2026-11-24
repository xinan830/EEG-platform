## Context

See [proposal.md](proposal.md) for motivation. The current review analysis path is instantiated as a `PsdReviewAnalysisProvider` and uses `ReviewAnalysisBinding` to quantize samples and start loads. `RecordingReviewViewModel.Navigation` calls `PreviewAtSeconds` from both playback ticks and cursor-position updates; `EndTimelineDrag` only changes navigation/playback state. The backend already exposes full Run and backend-planned on-demand window paths with immutable result identity, bounded scheduling and per-consumer subscriptions.

The existing `review-algorithm-link` contract includes throttled drag reads, while this change explicitly replaces that trigger behavior with deferred analysis during drag and an immediate final-target request on release. Playback continues to use backend policy and the existing sample cursor.

## Goals / Non-Goals

**Goals:**
- Make playback, drag, and drag-release triggers deterministic and testable.
- Separate algorithm-neutral review lifecycle coordination from algorithm-owned request mapping and result presentation.
- Keep the analysis target bound to a complete immutable scientific snapshot and backend window identity.
- Establish a repeatable, acceptance-gated procedure for adding algorithms to review.

**Non-Goals:**
- Change any algorithm formula, scientific preprocessing, backend window planner, or backend API in the first implementation phase.
- Change acquisition data, raw recordings, montage snapshots, full Run semantics, or stored artifact formats.
- Make every catalog algorithm automatically available for review.
- Change backend scientific window policy to force a result for every cursor tick. A one-second target refresh is not a guarantee that a newly computed scientific result exists or completes within one second.
- Treat engineering verification as clinical validation.

## Decisions

### One trigger owner, three explicit input events

The review ViewModel remains the source of the recording cursor and reports three distinct events to an algorithm-neutral review coordinator: playback sample advancement, drag movement, and drag completion. Drag movement updates navigation/waveform state but does not submit drag-originated analysis targets. Drag completion submits the current integer cursor sample once. While playback is active, each one-second refresh tick submits the then-current integer cursor sample even if the backend resolves it to an already-known window; the coordinator/adapter may deduplicate identical ready or in-flight identities. Playback cadence is independent of waveform loads and drag analysis suppression. The backend remains authoritative for whether a policy-eligible scientific window exists.

Alternative considered: retain a time-based throttle during dragging. Rejected because it produces scientific requests for transient pointer positions and conflicts with the user's chosen interaction. The trade-off is that a long drag displays the last result as historical until release; the UI must label the actual result range and pending final target clearly.

### Adapters compose algorithms; they do not implement science

Define a review adapter contract for support declaration, immutable request-snapshot creation, and typed response projection/presentation. Generic coordination owns target revision, panel lifetime, cancellation/subscription detachment, status lifecycle and stale-response rejection. The algorithm adapter/presenter owns algorithm-specific configuration shape, arrays/units, quality display and provenance fields. Backend manifests/runners remain authoritative for eligibility, window bounds, preprocessing and calculation.

Alternative considered: place a `switch(algorithm_id)` in the review coordinator or instantiate the entire algorithm catalog workspace. Rejected because it centralizes algorithm-specific branches or couples review to unrelated full-run UI. The adapter registry is closed and explicit: catalog presence alone does not enable review.

### Snapshot analysis inputs and keep display processing separate

Each adapter creates an immutable snapshot containing `recording_id`, registered source checksum, integer cursor sample, analysis montage/reference identity, complete normalized algorithm config, algorithm-owned preprocessing config, definition/scientific/implementation identity when available, and window policy version. Display montage/filter state is not included unless the specific algorithm contract explicitly declares that it changes science. Backend-returned actual range, cursor, quality and provenance are the displayed source of truth. The client must not claim complete snapshot validation until all required identities are present and compared with the backend response.

Alternative considered: reuse the review waveform's filtered chunks or current display montage as analysis input. Rejected because the review rendering pipeline is disposable/display-oriented and would risk changing scientific input identity. Any mismatch between current API metadata and the snapshot requirements is recorded as a blocker requiring a separately specified backend contract change, not a silent client guess.

### Reuse full Run and on-demand window lifecycles

Use the backend window endpoint for supported dynamic playback targets, maintaining its backend planning, deduplication, queue bounds, artifact integrity and subscription lifecycles. Keep full Run creation for static or explicitly user-requested complete analysis where appropriate. No per-tick full Run, new cursor clock, WPF scientific computation, or client-selected window bounds are introduced.

Alternative considered: create one full Run for every cursor position or put all computation into the desktop. Both break responsiveness, resource bounds and the existing scientific ownership boundary.

### Stage adapter rollout behind acceptance

First adapt existing PSD review behavior to the generic contract and establish trigger/snapshot/stale-result tests. Then review each algorithm's declared window capability, request configuration, scientific equivalence evidence, quality/provenance shape and chart/presenter before registering it. Algorithms without a validated backend window executor remain unavailable for playback review; no automatic fallback to a full Run is allowed.

## Risks / Trade-offs

- [Release-only drag update can feel delayed on long gestures] → Keep cursor and waveform immediate, label the previous result's actual range, show the final target as pending, and request immediately on release.
- [One-second playback target may conflict with per-algorithm window eligibility or compute latency] → Submit the current cursor on every one-second target tick, let backend policy resolve the eligible window, and expose queued/computing/lag separately from true insufficient data. Keep old results labeled by their returned range.
- [Snapshot lacks enough source identity in the current desktop model] → Use backend returned identity and explicit immutable config; if required identity cannot be obtained, block reuse and specify an API change before implementation.
- [Algorithm result shapes differ] → Keep typed parsers and presenters in algorithm-owned adapters; reject malformed axes/units/provenance rather than coercing them in generic code.
- [Sharing cancellation could stop another panel's work] → Cancel only this consumer's subscription and foreground read; preserve backend shared work under its current subscription contract.
- [Scope may cause review orchestration files to exceed 400 lines] → Inspect file-size policy before edits, split by state ownership when justified, and update `docs/code-size-policy.json` for any file remaining above 400 lines.

## Migration Plan

1. Add or update review contract tests and adapter abstractions while retaining PSD as the sole registered review algorithm.
2. Switch playback/drag/release triggers to the explicit policy and verify latest-target cancellation and backend subscription detachment.
3. Move PSD config mapping and result projection behind the PSD adapter without changing the scientific request or numerical output.
4. Register further algorithms individually only after their backend on-demand window capability and review presenter pass the declared acceptance criteria.
5. Rollback consists of disabling/unregistering the new review adapter path and restoring the prior PSD review route; backend windows, completed Runs, artifacts, and raw recordings require no migration or deletion.

The on-demand window API now returns `scientific_configuration_json`: canonical UTF-8 JSON containing the backend-normalized algorithm config and resolved scientific context used to produce `configuration_sha256`. This is an additive response contract change consumed by the WPF review adapter. The context is limited to calculation inputs and provenance; unstable exception text is excluded. The desktop verifies the SHA-256 over the exact canonical bytes and confirms the submitted config is contained with equal JSON values before binding results. Persisted Runs and window artifacts remain untouched.

## Validation Strategy

- Desktop tests for playback target cadence, drag suppression, release flush, same-target deduplication, backward seek, cancellation detachment, stale responses and panel/recording lifecycle.
- Adapter contract tests for complete snapshot serialization, identity matching, algorithm-specific axes/units/quality/provenance parsing and explicit unsupported capability.
- Backend contract/equivalence tests are required only if an implementation change touches backend behavior; this plan itself does not alter science.
- Build and run applicable WPF tests, validate OpenSpec strictly, run `git diff --check`, and inspect affected source file sizes before completion.
- Manual acceptance confirms the cursor stays responsive, the final drag position is requested on release, playback does not block, and the displayed result identifies its actual backend range and quality.
