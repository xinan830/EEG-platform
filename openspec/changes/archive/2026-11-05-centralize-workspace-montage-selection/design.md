## Context

See proposal.md and the modified specs. The desktop already has a persisted montage catalogue and immutable per-record montage snapshots. Current consumers nevertheless expose separate selection state: `RecordingReviewSession.SelectedViewingMontage` and `AlgorithmListViewModel.SelectedMontageProfile`; review code also copies or projects selection changes between the display and algorithm parameter surfaces. Catalogue ownership and current-selection ownership are separate responsibilities and must remain so.

The engineering contract requires one owner for shared mutable state and explicit sample/channel mapping provenance. The user's decision is that a workspace creates the state instance and all workspace consumers read it; the instance is isolated by recording/workspace.

## Goals / Non-Goals

**Goals:**

- Give each recording-review workspace one writable selection of a validated immutable montage snapshot.
- Inject the same state instance into waveform and algorithm consumers, including every algorithm panel in that workspace.
- Retain separate waveform and algorithm processing, cache, and lifecycle ownership.
- Make montage identity and selection revision part of consumer freshness and request construction.
- Remove duplicate writable selections and event-copy synchronization rather than preserving them behind adapters.

**Non-Goals:**

- Do not merge waveform filtering/projection with backend scientific processing.
- Do not change acquisition-time montage selection, montage catalogue persistence, hardware REF/GND semantics, raw samples, algorithm formulas, or algorithm-specific input roles.
- Do not share one live selection between unrelated workspaces or recordings.
- Do not rewrite completed historical Runs or silently reinterpret their provenance.
- Do not add compatibility selectors or keep old display/analysis selection paths active.

## Decisions

1. **Selection owner is a workspace-scoped instance, not a process-wide singleton.** The desktop composition layer provides the montage-selection capability and creates one instance when a recording-review workspace is opened. It binds that instance to the recording identity and validated montage catalogue. This keeps the platform contract common while preventing one recording's selection from changing another's.

   Alternative considered: a global current montage. Rejected because simultaneous or sequentially opened recordings could leak selection across recording boundaries and the state would lack a clear lifecycle owner.

2. **The workspace passes one shared selection reference to all consumers.** The waveform session and algorithm-panel context observe the same selected immutable snapshot and monotonically changing revision. Consumers own subscriptions, local derived data, cancellation, and caches, but cannot mutate or shadow the selection. The existing montage catalogue/store remains responsible for available and persisted snapshots, not the current selection.

   Alternative considered: keep each selection and synchronize through events. Rejected because event forwarding permits transient divergence, duplicate ownership, and stale request construction; it is the behavior this change removes.

3. **A selection change is an input identity change, not an in-place mutation of a result.** Consumers invalidate in-flight work or reject late completions unless their captured recording identity, montage snapshot fingerprint, and selection revision still match. Existing cache keys must continue to include all signal-affecting inputs (absolute sample range, channel order, montage mapping, and display-processing parameters where applicable). Completed Runs remain inspectable with their original provenance but are not current for the new selection.

   Alternative considered: let consumers update their state and refresh opportunistically. Rejected because old waveform frames or algorithm results could be shown under the new selection.

4. **Algorithms retain role selection, not montage selection.** Algorithm-specific channel subsets and semantic roles remain local to each algorithm when its contract requires them. They are resolved against the shared montage snapshot and carried in request order with explicit mapping identity. A missing role is a structured unavailable state; no fallback to a former snapshot, positional channel, or another montage is allowed.

   Alternative considered: force all algorithms to use the same exact output channel list. Rejected because algorithms legitimately require different roles and channel counts; the unified setting is the montage mapping, not a single universal analysis-channel list.

5. **Keep the existing API/provenance contract unless implementation inspection proves it cannot express the selected snapshot.** Current desktop official Run requests already carry explicit montage and resolved outputs. The implementation should source those fields from the workspace instance and must not add a legacy request path. If selection revision itself is not required server-side for stale client results, use it locally and keep the request fingerprint based on stable scientific inputs; do not create a gratuitous backend/API migration.

## Risks / Trade-offs

- [Risk] A consumer can keep a hidden copy after the selector is removed → enumerate all review request builders and property-change handlers; test that every panel's request reflects one selection revision.
- [Risk] Rapid selection changes can allow old asynchronous results to reappear → require captured-revision checks at publication points and test late completion ordering.
- [Risk] Existing display caches may omit snapshot identity → inspect cache key construction and add exact snapshot identity wherever a cache can return montage-derived values.
- [Risk] The existing specification explicitly required display/analysis independence → this change intentionally replaces that user-visible behavior within a recording-review workspace; acquisition snapshots and historical Run provenance remain separate and immutable.
- [Risk] A montage may support waveform display but not a given algorithm role → show waveform if valid while marking only the dependent algorithm unavailable, with a structured reason.

## Migration Plan

This is an in-memory desktop ownership migration; no persisted data migration is expected. Build the workspace-scoped state from the selected recording's validated catalogue, inject it into waveform and algorithm consumers, move the single user-facing selector to the workspace owner, then delete the consumers' independent selection properties and synchronization handlers. Keep current API fields populated from the shared snapshot. Rollback is a source revert before release; do not restore two active selectors as a compatibility mode.

## Validation Strategy

Use focused desktop tests for workspace isolation, shared selection propagation, role mapping, missing roles, cache identity, and stale asynchronous completion. Verify official Run request payloads use the same montage snapshot and ordered resolved channels displayed by the workspace. Run the relevant desktop test project and compile the WPF project; do not claim backend/scientific validation because formulas and backend computation are unchanged.
