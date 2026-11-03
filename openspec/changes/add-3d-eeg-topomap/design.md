## Context

See proposal.md for motivation. The existing 2D resolver uses its MNE projection and montage `MapX`/`MapY` snapshots. The 3D mapping uses `mne.colin27_1020` in MNE `head` coordinates and meters; these contracts remain separate. The current 2D topomap computes a 65x65 inverse-distance grid in the backend. The desktop has no installed 3D rendering package.

The accepted mapping is the user-provided `colin27_1020` schema-v4 file, bundled with the GNM skin mesh and Apache-2.0 license/attribution. Its source is MNE `head` coordinates in meters; target coordinates remain uncalibrated GNM model units. The mapping is marked `measured contour fractions; anatomical review required`, so it supports standard-template visualization, not individualized or clinical localization.

## Goals / Non-Goals

**Goals:**

- Add a shared and traceable MNE-to-GNM location path that is independent of editable 2D diagram positions.
- Let Channel Settings inspect the same standard 3D locations beside its existing editable 2D diagram.
- Use existing algorithm values to build a bounded 3D scalp field, with backend-owned spatial processing and a reusable desktop renderer.
- Integrate RBP as the first consumer while preserving the existing 2D topomap.

**Non-Goals:**

- Changing RBP formulas, preprocessing, quality rules, algorithm version, or stored results.
- Patient-specific electrode localization, clinical interpretation, real-time acquisition, or automatic inference for unsupported labels.
- Migrating every algorithm to 3D in this change.

## Decisions

1. **The user-provided mapping file is the only 3D coordinate authority.** Validate its source montage, source frame/unit and any transform, exact site coverage, target model and mesh identity, target frame/unit, and intended use. Add no fallback parser for the current approximate demo file. Keep the 2D resolver and `MapX`/`MapY` behavior unchanged.

2. **Reuse algorithm output and keep spatial field construction in the backend.** The RBP adapter supplies its existing per-channel band arrays, order, units, quality, and absolute window. A separately versioned `gnm_upper_scalp_gnomonic_linear_v1` method projects the validated upper scalp into the landmark frame, uses deterministic linear triangles, and evaluates only mesh vertices inside the contributing electrode convex hull. It does not rerun EEG science or extrapolate beyond the supported hull. Exact source values remain in the electrode records.

3. **Keep 3D responses separate from the current 2D response.** Add typed 3D location, model, and topomap contracts so the larger mesh-oriented payload cannot affect existing 2D callers. Responses identify model/mapping hashes and a stable mesh vertex order. The desktop loads a model once by verified identity and reuses it across playback windows.

4. **Share one model client and renderer across both consumers.** The RBP panel and Channel Settings use the same cached model response and GNM mesh identity. Channel Settings shows only currently enabled EEG inputs with mapped sites; this view is read-only. Manual `MapX`/`MapY` edits and REF/GND hardware markers remain in the 2D diagram and are never converted into 3D coordinates.

5. **Keep the camera and rotation pivot on one origin.** Normalize display geometry around the mesh-bounds center and use that normalized origin for both the camera target and model rotation. Enclose the complete scene in a sphere about that origin; zoom by changing field of view while the camera stays outside the sphere. Channel Settings represents mapped positions with electrode markers only; the adjacent channel list provides names without floating 3D labels or connector lines.

6. **Start with the WPF platform 3D APIs.** Use `Viewport3D`/`MeshGeometry3D` already provided by WPF; add no rendering package unless a measured prototype shows the GNM mesh cannot meet interaction and playback performance. Keep parsing/scene creation and updates off the UI dispatcher, and publish only complete current-generation visuals. Place the 3D view as a separate selectable presentation in the existing RBP topomap host; retain the 2D view.

7. **Treat model assets as product dependencies.** Bundle the validated GNM mesh, mapping, Apache-2.0 license, and attribution. Mapping SHA-256 is `a973409df056b801113461c0e40d4381751217921505c3b64201cfb682b0f5d8`; mesh-file SHA-256 is `5bf5155d05b5b5cd377dc2870454f9291ba1e66726cd6ef2cc1bb636398bcabd`; canonical vertex/triangle geometry SHA-256 is `67e9c419ddeaab84dafceabd835e134cb30e5b5bfbc048da6b339c7d373f0a46`. Reject a mesh/mapping hash mismatch rather than silently transforming coordinates.

## Risks / Trade-offs

- [The mapping's proportional locations may not match individual anatomy] -> Keep the standard-template warning visible and do not describe target units as metric or the locations as clinically verified.
- [Surface interpolation can visually differ from the existing 2D map] -> Record it as a separate spatial method, preserve the source EEG values exactly at mapped electrodes, and compare synthetic known fields plus a fixed RBP fixture.
- [A full mesh field or scene update may increase per-window cost] -> Reuse the static mesh and precomputed geometry, benchmark at the existing playback update rate, and keep the 2D view usable if 3D is unavailable.
- [A wrong or stale model asset could misplace colors] -> Verify mapping/model content identities and vertex order before rendering.
- [Channel Settings appears to disagree with the algorithm view] -> Use the same model client and renderer; report labels absent from the standard mapping instead of deriving a 3D point from 2D edits.
- [GNM distribution requirements can be lost during packaging] -> Include and test the required license and attribution with the bundled asset.

## Migration Plan

No database migration is required. Existing RBP results and 2D topomap APIs remain unchanged. Add the 3D asset/mapping and typed API path, then enable the RBP and Channel Settings 3D views only after mapping/model validation. Rollback is limited to disabling the new 3D views and routes; the existing 2D map and stored analysis results remain readable.

## Validation Evidence

- GNM reader `read_full_mapping` and core `verify_mapping` passed against the supplied mapping and the exact bundled GNM mesh; all 94 source coordinates matched MNE 1.13.2 `colin27_1020` `head` coordinates exactly.
- There are 94 named channels and 90 distinct spatial coordinates; T3/T4/T5/T6 are aliases of T7/T8/P7/P8. REF/GND are not standard scalp sites. A1/A2/M1/M2 are represented in the mapping but excluded from the interpolated scalp field.
- The mapping's own accuracy status requires anatomical review. Passing asset and mesh checks validates identity and geometry, not anatomical accuracy of every proportionally placed electrode.
