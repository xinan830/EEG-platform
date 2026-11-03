## Context

See proposal.md and specs/analysis/eeg-topomap/spec.md. The current WPF preparer derives the legend range from every incoming grid, while the playback view model preserves stable topomap control instances.

## Goals / Non-Goals

**Goals:** Keep color meaning stable across windows without scanning an entire recording or changing backend results.

**Non-Goals:** Define clinical thresholds, change RBP formulas, or make absolute-power scales comparable across separate runs.

## Decisions

- Use `0–1` for ratio data because that is the declared mathematical domain.
- For absolute power, cache the first ready grid range by run, band, and unit in the canvas presentation. Reset the cache when the run identity changes. This avoids hidden backend work and makes each playback run temporally comparable.
- Pass the run identity through the existing Review view-model binding; do not add API fields because the response already includes `run_id`.
- Continue using the locked bounds for both raster color projection and contour levels. Clamp only in the existing presentation normalization.

Alternatives considered: per-window autoscaling was rejected because colors change meaning; a global numerical absolute-power maximum was rejected because no scientifically justified shared bound exists in the current contract; scanning a whole recording was rejected because it adds I/O and latency.

## Risks / Trade-offs

- [First window is unrepresentative] → Later values may saturate; keep the endpoints visible and reset calibration only for a new run. Bounds are presentation calibration, not quality thresholds.
- [Separate runs are not directly comparable in absolute mode] → State that explicitly in the UI contract; do not imply cross-run calibration.

## Migration Plan

No data migration or API rollout is required. Existing result payloads remain unchanged. Revert the presentation policy to restore prior visual autoscaling if needed.
