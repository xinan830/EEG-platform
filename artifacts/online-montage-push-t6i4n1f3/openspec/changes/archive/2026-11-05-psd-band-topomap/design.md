## Context

See proposal.md and the modified EEG topomap requirements. PSD already returns ordered channel-by-frequency density over the review window, and the shared `band_power` primitive interpolates exact band edges then applies trapezoidal integration. RBP returns separate precomputed relative/absolute band arrays.

## Goals / Non-Goals

**Goals:** Make five-band PSD topomap selection use one canonical frequency-band declaration and backend-computed band-mean PSD, with explicit identity and units.

**Non-Goals:** Change PSD estimation, filtering, quality gates, the PSD frequency spectrum chart, RBP calculations, or RBP topomap behavior.

## Decisions

- Define the canonical five bands once in the existing scientific analysis contract and have PSD band-share and RBP calculations consume those ranges.
- Add PSD band field IDs (`psd.band-mean-density.<band>`). The PSD adapter validates one ID, integrates each channel's PSD with the existing `band_power`, divides by the full band width, and retains `V^2/Hz`.
- Keep the existing shared dynamic topomap API and spatial interpolation. Algorithm-specific adapters continue selecting the source data; PSD never reads RBP arrays.
- Keep the PSD selector options in the PSD review model as labels and IDs only. It sends the selected field ID; no PSD calculation moves to WPF.
- Keep exact frequency-bin field IDs available to API consumers while the Review selector exposes only the five canonical PSD band-mean-density fields.

## Risks / Trade-offs

- [A requested band may exceed the PSD output frequency axis] -> Return a structured unavailable/error response; never clip or substitute a nearby band.
- [A frequency axis may be nonuniform] -> Interpolate PSD at exact band edges, integrate the piecewise-linear density, and divide by the complete canonical band width.
- [The map value is a band summary, not total band power] -> Keep the API value in `V^2/Hz`, report the exact frequency range and averaging operation, and convert only the scale labels to `µV²/Hz`.

## Maintainability Assessment

`backend/app/services/topomap.py` is 525 lines and combines algorithm-specific result adapters with shared spatial projection. The adapters already have explicit class/protocol boundaries, but this change does not require changing ownership or moving unrelated RBP logic. Track extracting the adapters into a dedicated module when another adapter behavior changes; avoid a size-only split in this focused fix.

## Migration Plan

No stored PSD/RBP result or database row is rewritten. PSD Review requests one of the five band-mean-density field IDs; exact frequency-bin API requests remain valid. The former `psd.band-power.<band>` field IDs are rejected so absolute power cannot be mistaken for PSD. Rollback restores the prior single-bin Review selector without modifying saved spectra.
