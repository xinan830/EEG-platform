# Shared 3D Electrode-Position Preview

## Why

Channel Settings needs a read-only 3D view of standard electrode locations on the registered GNM head model. The current RBP 3D scalp-field view is visually unsatisfactory, so this change keeps the validated model and coordinate capability for Channel Settings and removes RBP's 3D field presentation from the active product.

## What Changes

- Keep a versioned mapping from exact MNE colin27_1020 locations to the declared GNM head model.
- Expose typed model and electrode-location responses with mapping, mesh, coordinate-frame, and unit identity.
- Keep the editable 2D channel-position diagram and add a separate read-only 3D standard-location view for enabled EEG channels.
- Remove the RBP 3D field selector, playback refresh path, surface interpolation endpoints, and field-rendering contract. The existing 2D RBP topomap remains available.

RBP 3D scalp-field visualization is explicitly deferred. It is not part of the current shipped surface and must be proposed again only after its projection, coverage, and visual acceptance criteria are redesigned.

## Capabilities

### Modified Capabilities
- desktop/electrode-locations: Expose validated standard 3D electrode locations alongside the existing 2D configuration view.

## Impact

Expected code areas are the GNM mapping/model asset loader and typed location API, the Channel Settings model client and position window, and the shared WPF head-model renderer. RBP's 3D-only API, calculation, view, and contracts are removed; RBP's 2D topomap and saved algorithm results are unchanged. No database migration or scientific algorithm change is required.
