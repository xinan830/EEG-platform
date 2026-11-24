## Why

Official BrainBeat currently reports an IAPF-relative ratio of relative band powers, while the literature-named BrainBeat is described as a fixed-band Fz theta to Pz alpha absolute-power ratio. The same public identity therefore risks implying method equivalence that the current formula does not provide.

## What Changes

- **BREAKING**: Make `brainbeat` the explicitly defined classic fixed-band absolute-power ratio and advance its scientific and implementation identities so new results cannot reuse old calculations.
- Give the existing IAPF-relative calculation a separate official algorithm identity and label it as an individualized experimental variant; keep it independent of the classic result.
- Declare calculation formula, band boundaries, units, quality behavior, dynamic window policy, channel order, and provenance in backend manifests and catalog metadata.
- Update Official Run and recording-review requests/presentation to select and display the two variants without moving scientific calculation into WPF.
- Preserve already stored Run records and artifacts as immutable historical results; do not add a retired calculator or result converter.
- Validate the literature method details and numerical behavior with focused source-backed and synthetic-signal tests before treating the classic variant as literature-equivalent.

## Capabilities

### New Capabilities
- `analysis/classic-brainbeat`: Defines the fixed-band absolute-power BrainBeat calculation and its traceable execution contract.

### Modified Capabilities
- `analysis/official-algorithm-catalog`: Exposes distinct identities and parameters for classic and individualized BrainBeat.
- `analysis/official-algorithm-execution`: Defines Run inputs, outputs, quality states, window policy, and historical-result boundaries for both variants.
- `desktop/on-demand-dynamic-review`: Allows review to select a BrainBeat variant and submits the selected variant's declared window policy.

## Impact

- Backend: BrainBeat modules/manifests, official algorithm request model and runtime registration, shared band-power computation, catalog and result provenance.
- Desktop: algorithm catalog/detail integration, review parameter serialization and BrainBeat panel labeling/selection.
- Tests: synthetic known-band calculations, catalog identities/defaults, Run and on-demand parity, and desktop request serialization/presentation contracts.
- Documentation: scientific definition and evidence limits. The user-provided 4–8 Hz Theta and 8–12 Hz Alpha are the initial target bands; confirm these values and the absolute-power interpretation against the article's methods before claiming exact reproduction. Keep the shared four-second Hann Welch segment with 50% overlap unless independent evidence requires a separately versioned estimator.
- Non-goals: changing global preprocessing, adding new Welch parameters, changing unrelated algorithms, rewriting historical stored results, or claiming clinical validation.
