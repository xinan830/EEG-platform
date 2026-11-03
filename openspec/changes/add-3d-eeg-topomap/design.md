## Context

See proposal.md for motivation. The current backend resolves standard XYZ from `mne.colin27_1020` in `mri` coordinates and meters; its 2D projection is separate. The current 2D topomap consumes montage `MapX`/`MapY` snapshots and computes a 65x65 inverse-distance grid in the backend. The desktop has no installed 3D rendering package.

The existing GNM candidate mapping is explicitly marked as an approximate demo, uses MNE `head` coordinates, and labels target coordinates as GNM model units rather than meters. It cannot be used as the production mapping. The user will provide the standard mapping file before implementation proceeds.

## Goals / Non-Goals

**Goals:**

- Add a shared and traceable MNE-to-GNM location path that is independent of editable 2D diagram positions.
- Use existing algorithm values to build a bounded 3D scalp field, with backend-owned spatial processing and a reusable desktop renderer.
- Integrate RBP as the first consumer while preserving the existing 2D topomap.

**Non-Goals:**

- Changing RBP formulas, preprocessing, quality rules, algorithm version, or stored results.
- Patient-specific electrode localization, clinical interpretation, real-time acquisition, or automatic inference for unsupported labels.
- Migrating every algorithm to 3D in this change.

## Decisions

1. **The user-provided mapping file is the only 3D coordinate authority.** Validate its source montage, source frame/unit and any transform, exact site coverage, target model and mesh identity, target frame/unit, and intended use. Add no fallback parser for the current approximate demo file. Keep the 2D resolver and `MapX`/`MapY` behavior unchanged.

2. **Reuse algorithm output and keep spatial field construction in the backend.** The RBP adapter supplies its existing five per-channel band arrays, order, units, quality, and window. A separately versioned 3D spatial method returns values aligned to a named mesh; it does not rerun EEG science. The method and scalp-region boundary are finalized after the standard mapping and target mesh are inspected, then recorded in the API provenance and tests.

3. **Keep 3D responses separate from the current 2D response.** Add typed 3D location, model, and topomap contracts so the larger mesh-oriented payload cannot affect existing 2D callers. Responses identify model/mapping hashes and a stable mesh vertex order. The desktop loads a model once by verified identity and reuses it across playback windows.

4. **Start with the WPF platform 3D APIs.** Use `Viewport3D`/`MeshGeometry3D` already provided by WPF; add no rendering package unless a measured prototype shows the GNM mesh cannot meet interaction and playback performance. Keep parsing/scene creation and updates off the UI dispatcher, and publish only complete current-generation visuals. Place the 3D view as a separate selectable presentation in the existing RBP topomap host; retain the 2D view.

5. **Treat model assets as product dependencies.** Bundle the selected GNM mesh and the license/attribution required by its source. Keep one canonical model asset identity shared by backend calculations and desktop rendering; reject a mesh/mapping hash mismatch rather than silently transforming coordinates.

## Risks / Trade-offs

- [The supplied mapping may use a different frame, units, or target mesh than the current backend] -> Validate metadata and transform against canonical MNE sites before coding; do not convert by scale guesses.
- [Surface interpolation can visually differ from the existing 2D map] -> Record it as a separate spatial method, preserve the source EEG values exactly at mapped electrodes, and compare synthetic known fields plus a fixed RBP fixture.
- [A full mesh field or scene update may increase per-window cost] -> Reuse the static mesh and precomputed geometry, benchmark at the existing playback update rate, and keep the 2D view usable if 3D is unavailable.
- [A wrong or stale model asset could misplace colors] -> Verify mapping/model content identities and vertex order before rendering.
- [GNM distribution requirements can be lost during packaging] -> Include and test the required license and attribution with the bundled asset.

## Migration Plan

No database migration is required. Existing RBP results and 2D topomap APIs remain unchanged. Add the 3D asset/mapping and typed API path, then enable the RBP 3D view only after mapping/model validation. Rollback is limited to disabling the new 3D view and routes; the existing 2D map and stored analysis results remain readable.

## Open Questions

- The standard mapping file is pending from the user. Inspect its source frame/unit, transform, target model/mesh identity, site coverage, and declared scalp region before implementing a parser or selecting the surface interpolation and renderer input representation. If those semantics are absent, stop and ask rather than infer them.
