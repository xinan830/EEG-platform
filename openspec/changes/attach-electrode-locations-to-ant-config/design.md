## Context

See proposal.md for motivation. Channel entries already carry optional standard MNE XYZ metadata and manual 2D diagram positions. The resolver client is injected into `ChannelConfigurationWorkspaceViewModel`; ANT's generated system profile currently comes from `ChannelMappingViewModel` without those snapshots. Montage profiles embed a channel-configuration snapshot and formula rows; the direction map projects arrows from these two sources.

## Goals / Non-Goals

**Goals:**
- Attach authoritative MNE locations to the generated ANT configuration when resolution succeeds.
- Preserve the location-bearing channel snapshot in montage profiles and derive arrows only from montage formula rows.
- Keep the configuration usable when the backend is unavailable and report unavailable positions clearly.

**Non-Goals:**
- Add a second coordinate table to WPF or infer missing electrode locations.
- Change channel order, display inclusion, sample values, montage formulas, or electrical reference semantics.
- Assign scalp XYZ to REF/GND, whose independent 2D landmarks are already in the channel profile.

## Decisions

- Resolve named ANT scalp labels through the existing typed backend endpoint while preparing the default configuration in the channel workspace. This keeps MNE as the sole standard-coordinate source; embedding constants or resolving independently inside the direction map would duplicate ownership.
- Treat resolver failure as a display-metadata failure: preserve the ordinary ANT profile and channel functions, leave locations unavailable, and show a clear status. Do not cache a silent fallback or fail configuration loading.
- Keep direction as a projection of `DerivedMontageChannel` formula data and the selected channel snapshot. Do not persist arrows, because coordinates and formulas can change independently and would otherwise become stale.
- Existing JSON remains compatible because location fields are optional. Regenerated read-only ANT profiles obtain locations at runtime; saved user profiles and historical montage snapshots retain their own existing immutable location metadata.

## Risks / Trade-offs

- Backend downtime means the ANT system profile has no spatial endpoints for that refresh → report the failure and keep non-spatial channel configuration usable.
- Resolving on every refresh adds a small local API request → issue one batched request for the ANT labels and avoid per-channel calls.
- Standard coordinates represent a template rather than cap/patient measurements → retain the existing source/version and standard-template labeling.

## Migration Plan

No data migration is needed. Older profile and montage JSON continues to deserialize with unavailable locations. Once the resolver is reachable, the generated ANT profile and newly created montage snapshots carry location metadata. Rollback consists of reverting the desktop behavior; stored snapshots remain readable because the fields are already supported.
