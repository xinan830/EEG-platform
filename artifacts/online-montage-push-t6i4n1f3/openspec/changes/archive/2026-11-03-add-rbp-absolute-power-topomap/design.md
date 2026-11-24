## Context

RBP's shared frame currently computes five absolute band powers in `uV^2` for its diagnostic trace and exposes five ratio arrays as structured output. Static, dynamic, and on-demand execution all share that frame. The RBP topomap adapter consumes the structured result and resolves positions from the same montage snapshot used by the result window.

## Goals / Non-Goals

**Goals:**

- Publish both representations from the same frame and preserve per-channel availability and channel order.
- Let Review choose a representation and band without changing the RBP run or its time/montage identity.
- Compute only the requested topomap band when the user selects a single band, while keeping all five bands as the default.
- Separate RBP source/spectrum timing from band integration and topomap interpolation/serialization in request-correlated backend diagnostics.
- Keep existing topomap requests and stored relative results useful.

**Non-Goals:**

- No new algorithm catalog item, power computation, PSD read, or RBP rerun when the user changes the map selector.
- No reconstruction of absolute outputs from trace evidence and no database backfill.
- No changes to the RBP formula, filters, reference, quality policy, interpolation, or 3D topomap work.

## Decisions

1. **Extend the shared RBP frame output.** Add one `*_absolute_power` array per band, aligned to the existing channel arrays. Populate it from the `uV^2` values returned by the current band integration. Declare its units in static, dynamic, and on-demand contracts. Keep the existing `band_powers_uv2` trace for provenance, but make it unnecessary for consumers.
2. **Version the result contract, not the science.** Retain `official-rbp-v4`; advance the Definition from 2.0.4 to 2.0.5 and the implementation identity from `rbp-runtime-v5-channel-quality` to `rbp-runtime-v6-absolute-power-output`. The Definition graph remains a projection of the existing ratio formula; Definition output metadata and the structured result contract separately declare the public absolute arrays in `uV^2`. This separates new cache/artifact identities without claiming changed formulas.
3. **Select representation at the topomap boundary.** Add a `power_type` query with the closed values `relative` and `absolute`; omission means `relative`. The response records the selected value. The adapter chooses the corresponding structured array and sets `ratio` or `uV^2`; the band IDs remain delta/theta/alpha/beta/gamma.
4. **Keep old result behavior explicit.** Old static or dynamic results can still provide relative maps. An absolute request against a result without the declared absolute array yields five unavailable bands with a stable structured reason. The adapter never consults `calculation_trace` as a fallback.
5. **Make the WPF selector a view request.** Keep the existing band selector and add a separate representation selector. A representation change reloads the topomap for the current window through the existing request path; it does not create or mutate an AnalysisRun. Existing API consumers omitting `power_type` continue to receive relative maps.
6. **Filter spatial work by the selected band.** Add an optional closed `band_id` query to run and dynamic topomap endpoints. Omission returns all five bands; an explicit band returns only its map. The WPF band selector passes its value and reloads the same window. The selector does not alter the RBP result or trigger EEG calculation.
7. **Measure the backend stages at their owners.** Log per-window RBP spectrum-load and band-integration durations, plus per-band topomap interpolation and route serialization durations. Include request/window and recording identities, selected power type and band filter; never include signal samples or numeric output arrays. Existing source-preparation timings remain the boundary for cold continuous preprocessing.

### Alternatives considered

- Registering a separate absolute-power algorithm would duplicate the PSD and RBP execution and introduce a competing algorithm identity for the same frame; rejected.
- Reading `calculation_trace.band_powers_uv2` would make a diagnostic evidence shape an undocumented public API and would conceal old-result capability differences; rejected.
- Recomputing power in the topomap service would duplicate scientific work outside the RBP algorithm and could diverge from its quality and preprocessing contract; rejected.

## Risks / Trade-offs

- **[Existing result artifacts lack new arrays]** → Keep relative maps available and return an explicit unavailable response for absolute maps; do not backfill or infer values.
- **[Power values have a much larger numeric scale than ratios]** → Use the existing topomap response's per-band value range derived from selected values, and preserve `uV^2` through the WPF label/legend; do not normalize absolute values into ratios.
- **[Topomap refresh races during playback]** → Reuse the current window identity and generation checks; a late response for an older representation/window must not overwrite the active request.
- **[Timing logs are mistaken for scientific output]** → Keep them in operational logs only; do not add durations to result arrays, provenance identities, cache keys, or algorithm versions.

## Migration Plan

New RBP results use Definition 2.0.5 and the new implementation identity. Existing persisted Runs and dynamic windows are not rewritten. The API defaults omitted `power_type` to relative so current callers remain valid; explicit absolute requests for older results return structured unavailability. Rollback consists of reverting the code and this change before archive; no database migration or source recording changes are required.

## Validation Strategy

- Verify absolute arrays against the exact `uV^2` band powers already recorded by the shared frame, with ratio outputs numerically unchanged.
- Test static, multi-window dynamic, and on-demand array shape/unit/channel-order alignment, including one unavailable channel.
- Test API default/explicit representation, response unit, old-result unavailable behavior, and absence of trace fallback.
- Test API default/explicit representation and band filter, response units, old-result unavailable behavior, and absence of trace fallback.
- Test WPF query serialization for both selectors, single-band response presentation, unchanged current-window identity, and existing request-race behavior; build and run focused desktop tests.
- Capture backend timing logs and verify source preparation, RBP spectrum loading/integration, per-band interpolation and response serialization can be distinguished by request/window identity.
- Run OpenSpec strict validation and `git diff --check`.
- Record automated results and keep the change active until the Review UI is manually accepted.
