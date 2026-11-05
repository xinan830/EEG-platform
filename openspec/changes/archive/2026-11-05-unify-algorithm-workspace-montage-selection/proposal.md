## Why

The platform already provides `WorkspaceMontageSelection`, and recording review uses one instance for waveform and all review algorithm panels. The standalone algorithm workspace still stores `AlgorithmListViewModel.SelectedMontageProfile` independently, so its preview, Run, and role mapping can drift from the platform montage contract. This leaves two ownership models for the same capability.

## What Changes

- Create one `WorkspaceMontageSelection` instance for each active algorithm workspace recording context.
- Make algorithm workspace montage choices, role remapping, preview requests, and full Run requests read the instance's captured snapshot and revision.
- Remove the algorithm catalog's independently stored montage selection; expose only a projection over the workspace capability for binding.
- Release the instance and its subscriptions when the algorithm recording changes or the catalog is disposed.
- Add focused desktop tests for instance isolation, selection propagation, request provenance, and stale selection rejection.
- **BREAKING** Algorithm workspace selection state is no longer an independent catalog field; all consumers use the workspace-scoped public montage capability.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `desktop/workspace-montage-selection`: extend the workspace-scoped capability contract to standalone algorithm workspaces.
- `desktop/algorithm-montage-selection`: require algorithm workspace consumers to use a locally created public selection instance rather than independent selection state.
- `desktop/wpf-official-algorithms`: require standalone algorithm Run and preview requests to use one captured selection snapshot and revision.

## Impact

- WPF algorithm catalog composition, recording registration lifecycle, request builders, and algorithm workspace view-model bindings.
- Focused desktop tests and the current OpenSpec delta; no backend formula, raw data, API schema, database, or scientific parameter changes.
- Existing recording montage snapshots and completed Run provenance remain immutable. Algorithm workspace instances are isolated from recording-review instances and from other recordings.
- No compatibility selector or second synchronization path is retained.
