## Why

Review currently follows the recording cursor but its analysis binding and panel are PSD-specific. Playback updates can trigger requests at the configured quantized step, while drag updates can still request analysis and releasing the drag does not explicitly submit the final target. This makes the desired low-latency, latest-cursor behavior unclear and makes adding verified algorithms to review require bespoke composition.

## What Changes

- Define review analysis as a shared adapter boundary: review owns cursor lifecycle, scheduling, cancellation and stale-result rejection; each algorithm adapter owns its normalized configuration, supported-window capability and result presentation.
- Make playback request policy explicit: during playback, request eligible windows as the cursor advances using backend policy; during timeline dragging, defer analysis requests and submit the final cursor target on release.
- Bind every request and result to a snapshot of recording identity, integer cursor sample, analysis montage/reference, algorithm parameters, preprocessing settings and backend-resolved window identity/range.
- Preserve backend ownership of algorithms, preprocessing, window boundaries, quality and provenance. Reuse existing full Runs and on-demand window sessions rather than creating a Run for each cursor tick.
- Stage algorithm adoption in review, beginning with the existing PSD path and enabling additional algorithms only after their window capability and review presentation pass explicit acceptance.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `openspec/specs/desktop/review-algorithm-link/spec.md`: Define cursor-trigger policy, final drag submission, immutable analysis input snapshots, and algorithm-neutral review coordination.
- `openspec/specs/desktop/on-demand-dynamic-review/spec.md`: Clarify playback/drag target scheduling and adapter responsibilities while preserving latest-target and subscription semantics.

## Impact

- WPF review cursor/navigation state, review analysis coordinator/binding, algorithm-owned review adapters and presenters, and their tests.
- Backend API and scientific algorithms are intended to remain unchanged; the plan reuses existing full Run and backend-planned on-demand window contracts. Any discovered need for a public API change must be separately specified before implementation.
- No raw recording mutation, migration of stored Runs/artifacts, new result compatibility layer, or clinical validation is included.
- The change alters user-observable request timing during dragging: analysis is requested on release, while playback continues to follow the backend-defined eligible-window policy.
