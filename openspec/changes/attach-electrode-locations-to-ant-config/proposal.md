## Why

ANT's built-in 28-input channel configuration currently has electrode labels but no standard-template coordinates, so the montage direction map cannot reliably place its endpoints. The user needs channel configuration to own electrode positions and montage formulas to retain their directed relationships against the same channel snapshot.

## What Changes

- Resolve standard-template locations for the generated ANT system configuration through the existing location service.
- Preserve resolved location metadata in montage channel-configuration snapshots and use it with formula direction for the direction map.
- Make resolver unavailability explicit while keeping ANT configuration usable without invented coordinates.

## Capabilities

### New Capabilities

### Modified Capabilities
- `desktop/electrode-locations`: ANT system configuration includes resolved standard-template positions where available.
- `desktop/montage-direction-map`: direction previews use the selected configuration snapshot's positions and the montage formula's directed endpoints.

## Impact

This affects the WPF channel-configuration refresh path and its montage snapshot consumer. It reuses the read-only `POST /api/electrode-locations/resolve` endpoint and MNE `standard_1020` source, adding `map_coordinates` to its response. The desktop model accepts a missing field as unavailable so an older backend response does not invent positions; correct standard-map rendering requires deploying the backend and desktop together. Existing persisted XYZ-only snapshots remain readable but unprojected until explicitly resolved again. EEG samples, channel ordering, and reference calculation do not change.
