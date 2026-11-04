# Design

## Scope

This change delivers a standard 3D electrode-position preview in Channel Settings. It does not render algorithm values on the GNM surface. The RBP 3D scalp-field view was removed because its spatial coverage and appearance did not meet the user's visual acceptance; any later algorithm surface view needs a new design and acceptance gate.

## Coordinate and Asset Contract

The user-provided mapping remains the sole 3D coordinate authority. It is validated against MNE colin27_1020 in head coordinates and meters, plus the registered GNM mesh identity and declared native model units. The 2D resolver and editable MapX/MapY remain independent. No 3D coordinate is inferred from a 2D position or approximate mapping.

The backend serves the immutable model, exact mapped sites, hashes, and coordinate frame through typed read-only endpoints. Channel Settings displays enabled mapped EEG sites, including mapped A1/A2/M1/M2 where configured. REF/GND and unmapped labels remain unavailable. Coordinate availability is kept separate from the scalp-field-site classification.

## Desktop Behavior

The existing 2D diagram remains editable. Its 3D view is read-only, uses the same validated model response, and shows markers without floating text labels; the adjacent channel list supplies names. The view loads and prepares model geometry away from the UI dispatcher, publishes only complete current-generation scenes, centers the camera and rotation pivot consistently, and bounds zoom so the camera cannot enter the model.

## Removed RBP Path

RBP no longer has a 3D band selector, playback-driven 3D refresh, surface-field interpolation route, field response model, or pseudocolor rendering path. Its current 2D topomap and algorithm results are unaffected. No dormant RBP 3D compatibility route is retained.

## Risks

- The mapping is a standard-template visualization and does not represent an individually measured cap or clinical localization.
- Channel Settings manual 3D acceptance on a real configured profile remains required.
- A future algorithm surface map must define field coverage, interpolation boundaries, edge treatment, units, and visual acceptance before implementation.
