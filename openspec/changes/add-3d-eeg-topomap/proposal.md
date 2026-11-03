## Why

The platform can place EEG values on a shared 2D scalp map, but it has no reusable path from the standard `colin27_1020` XYZ locations to a registered GNM head surface. A versioned 3D location and rendering contract lets RBP become the first consumer without recalculating its EEG results or treating editable 2D diagram positions as physical 3D coordinates.

## What Changes

- Add a shared, versioned mapping from exact MNE `colin27_1020` electrode sites to a named GNM head model and its declared coordinate frame and units.
- Add a typed 3D topomap result for existing per-channel algorithm values, including time window, units, included/excluded channels, mapping/model identity, spatial method, and structured unavailable reasons.
- Add a reusable desktop 3D scalp renderer and connect RBP as its first consumer; retain the existing 2D map as a separate view.
- Keep manual `MapX`/`MapY` positions confined to the 2D configuration diagram. A missing or invalid 3D mapping must not fall back to manual 2D or an approximate registration.
- Treat the user-provided standard mapping file as a required implementation and acceptance input. Confirm its schema, coordinate conventions, model identity, and provenance before adding a loader or shipping model assets.

## Capabilities

### New Capabilities
- `analysis/eeg-3d-topomap`: Versioned GNM surface topomaps built from existing algorithm outputs and rendered through a reusable desktop view.

### Modified Capabilities
- `desktop/electrode-locations`: Resolve and expose validated standard 3D GNM coordinates alongside the existing MNE XYZ and 2D projection, without conflating their sources or coordinate frames.

## Impact

Expected code areas are the backend electrode-location and topomap models/services/APIs, desktop electrode-location and topomap contracts, a shared WPF 3D renderer, and the RBP presentation. Tests must cover coordinate-frame/unit validation, exact channel matching, missing/duplicate mappings, invalid model identity, and unchanged RBP values. The GNM mesh and its required license/attribution may become application assets only after distribution rights and the standard mapping are verified.

This adds a separate 3D presentation contract. Existing 2D topomap routes, persisted algorithm results, scientific algorithm versions, units, and historical result reading remain unchanged. The feature is a standard-template visualization, not individualized electrode localization or clinical validation. No 3D fallback or old mapping compatibility path is added.
