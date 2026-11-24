## Context

See proposal.md for motivation. PSD Review currently binds one `TopomapCanvas` and sends one field ID. The backend PSD adapter rejects any request with more than one ID. RBP already demonstrates a five-map `ItemsControl` and repeated `field_ids`; the generic topomap response already carries multiple fields.

## Goals / Non-Goals

**Goals:** Reuse the current PSD result and topomap transport to display one or five band maps, use concise UI names, and keep exact band metadata accurate per field.

**Non-Goals:** Change PSD calculation, band edges, frequency-axis validation, unit conversion, interpolation, RBP values, or the HTTP request/response schema. Keep the existing first-window scale locking; repair the shared normalization within those bounds.

## Decisions

- Keep the five exact band definitions and computation in the backend PSD adapter; allow one or more unique explicit PSD field IDs and build each selected field independently from the same stored result.
- Store the band reduction and bandwidth provenance with each projected field. A multi-band request cannot place one band's bandwidth into shared provenance for every returned field.
- Keep API field labels and `frequency_range_hz` scientifically complete. Map field IDs to concise display-only names in the WPF canvas, and use concise selector options.
- Reuse RBP's stable topomap item collection and two-column layout. A single-band selection keeps the existing single-canvas layout; the all-band selection shows five maps and sends the five IDs together.
- Keep the topomap request bound to `CurrentAnalysisWindow`; no algorithm rerun, raw-signal reread, or frontend computation is introduced.
- Normalize colors using the actual nonzero display range. A fixed `1e-12` floor incorrectly compresses Gamma PSD values around `1e-14` to `1e-13 V^2/Hz`, even when their absolute-power equivalents span the full color scale. Unit-specific rendering branches would duplicate this rule, so correct the shared renderer once.
- Preserve equal display bounds for a constant field and use the midpoint color at that value. Later values below or above a locked constant range saturate at the respective endpoint. A zero-width scale has no contour levels.
- For validated average-reference outputs, use the formula's positive electrode as the spatial site. Keep channel-referenced and specified-pair outputs unavailable because their differential signal has no single justified scalp site.

## Risks / Trade-offs

- Five maps require more interpolation and rendering work than one map. The backend computes only the requested five projections from one PSD payload, and the existing shared canvas preparation remains in force.
- Average-reference values are displayed at their active electrode site; this spatial association does not change the signal reference or measured value. Bipolar and specified-pair outputs remain unavailable for topographic projection.
- Short labels omit the range visually. Exact Hz bounds remain available in each field's response metadata and the selector's canonical option-to-field mapping.

## Migration Plan

No database migration or API shape change is needed. Existing one-field requests and frequency-bin requests remain valid. Rollback consists of reverting the presentation and adapter changes; persisted PSD results are unchanged.

## Testing Strategy

WPF rendering regressions cover the observed small Gamma density range, equivalent density/power grids and their proportional color bounds, endpoint saturation, and zero/nonzero constant grids with exact legend bounds. Existing tests retain cancellation, frozen resources, and locked-scale behavior. Run focused PSD/RBP topomap suites and strict change validation; live Review acceptance remains with the user.
