## Context

`WorkspaceMontageSelection` already owns a recording-bound immutable montage snapshot and revision. Recording review injects one instance into waveform and algorithm-panel consumers. `AlgorithmListViewModel` instead keeps a private `selectedMontageProfile` and mutates channels directly from that field.

## Decisions

1. The algorithm workspace creates one `WorkspaceMontageSelection` after its registered recording catalogue is loaded. It defaults to the recording acquisition montage, then exposes the instance through the existing catalog context.
2. The public `SelectedMontageProfile` binding becomes a projection: its getter reads `selection.SelectedMontage`, and its setter calls `selection.Select`. It stores no separate montage value. The list `MontageProfiles` remains the validated catalogue, not current-selection state.
3. A selection-change handler maps algorithm-specific channel roles from the event's current snapshot, advances the local configuration revision, invalidates on-demand/full displays, and schedules the existing refresh path. The handler captures `WorkspaceMontageSelectionSnapshot` so one request cannot combine fields from different revisions.
4. Registration and disposal unsubscribe and release the selection before replacing the recording context. Existing algorithm-specific role/channel choices remain local; only montage identity and output resolution come from the selection instance.
5. `AlgorithmRunConfiguration` continues to serialize explicit montage identity, revision, fingerprint, and selected output mappings. It receives the captured selected profile from the catalog projection, so no API change is needed.

## Request and lifecycle flow

```text
registered recording
  -> validated montage catalogue
  -> algorithm-workspace WorkspaceMontageSelection instance
  -> selected snapshot/revision
  -> role/channel remapping and request payload
  -> backend scientific execution
```

The review workspace and algorithm workspace each own an instance. They share the public capability and rules, not mutable selection state.

## Risks and controls

- A late selection event can publish old results: use the existing configuration revision/supersede path and assert revision changes in tests.
- Existing unit tests instantiate the catalog without a recording: keep the catalogue collection usable for fixture setup, but only a registered recording can create a selection instance or submit a montage-bound request.
- A missing selected montage must remain explicit: do not fall back to a positional channel or an old selection.

## Non-goals

- No change to scientific formulas, preprocessing, backend algorithms, raw recording files, review workspace ownership, or persisted montage snapshots.
- No process-wide singleton and no sharing of the live selection between unrelated workspaces.
