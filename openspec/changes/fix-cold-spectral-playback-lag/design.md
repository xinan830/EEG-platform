## Context

See proposal.md for measured cold-cache costs. The backend deliberately serializes scientific jobs; increasing worker count risks memory/native-thread contention. Review previously combined source fetching, binding and projection in one deadline-triggered loop. Follow-up logs confirm that FAA/IAPF still use demand-only loading; FAA bypasses the shared continuous source cache and recreates its entire filtered recording in each window context.

## Goals / Non-Goals

Move cold work ahead of the playback deadline while keeping the shared waveform clock independent. Do not change science, filtering, reference, sampling or each algorithm's configured window/step policy (the measured spectral panels use 10 s / 1 s; FAA/IAPF use 20 s / 1 s). Do not promise zero initial cold-start latency or uninterrupted 2X playback if sustained computation is slower than consumption.

## Decisions

- Separate non-publishing preparation from validated adoption in the existing binding/coordinator. Reuse all scientific-identity checks at adoption; preparation cannot update the current binding or chart.
- A Review-owned buffer serially prepares at most two upcoming step-aligned pairs, including backend bins/range or canonical scalar projections. Keep one latest foreground target under overload and discard queued preparation older than that target before it starts. Future results cannot publish early.
- Retain the ordinary current-window request for initial open and missed preparation. Prepared projection reuse requires identical source objects and, for ranges, identical requested bounds; edits still use the existing backend projection route.
- Pause/seek/drag/close cancel and discard preparation. Cancellation is cooperative, with generation/reference fencing preventing late publication. Failed preparations are observed and exposed through the current-window error path rather than silently retried forever.
- Keep existing backend resource limits and subscription release. No new permanent desktop cache, dependency or public API. Backend artifacts retain their existing ownership and lifecycle.
- Log cold preparation duration separately from publication wait/hit and actual window lag. Tests gate fake cold jobs and verify deadlines, bounds, cancellation and identity; source/scalar scientific regressions remain applicable.
- Generalize the existing two-target preparation queue for both spectral pairs and scalar windows. FAA/IAPF reuse the binding's existing non-publishing prepare and validated adopt paths; TNK/stateful playback is excluded. One foreground target and one latest pending target prevent cancellation starvation when a deadline is missed.
- Extract continuous-source ownership from the spectral use case into one shared service using the existing bounded LRU/single-flight cache. FAA requests its ordered pair; full-recording zero-phase preprocessing remains unchanged and each window only slices that source. Source hash, montage/reference, filter, selected channel order, units and implementation remain in cache identity; no permanent cache or raw-data mutation is added.
- Completed terminal windows are immutable, so releasing their consumer subscription cannot gate delivery. Run bounded, exception-observed terminal cleanup independently; pending/cancelled observations still await cleanup to stop unwanted computation. Keep 100 ms discovery/backoff rather than increasing polling traffic.

Alternatives rejected: extra backend workers change resource governance without eliminating deadline-triggered loading; displaying the last result as current falsifies time; clearing caches or formatting changes do not fix scheduling.

## Risks / Trade-offs

- Preparation may miss deadlines or compete with other algorithms → bounded horizon, serial preparation and existing backend admission limits; retain honest pending/time labels.
- Range edits invalidate a prepared range → reuse bins only and project the current selection through the existing endpoint.
- Cold initial open still needs real calculation → report separately from steady playback.
- Preparing future windows can calculate/persist up to two additional pairs that the user never visits. Cancellation is cooperative; already executing backend work may finish, but cannot update a cancelled desktop binding.

## Migration Plan

No data migration. Deploy both backend source ownership and desktop scheduling changes. Roll back this source change to restore demand-only loading; existing artifacts remain valid. Real-recording cold-cache GUI performance must be remeasured after automated checks.
