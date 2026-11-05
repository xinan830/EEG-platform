## Why

PSD topomap labels include long frequency and calculation text, while the Review panel can request only one band at a time. Users need concise band names and a five-band view that compares the same PSD window directly.

## What Changes

- Show concise band labels such as “Alpha 波” in the PSD topomap selector and map title.
- Add “全部频段” to the selector and render Delta, Theta, Alpha, Beta, and Gamma maps together from the same dynamic PSD result window.
- Allow the PSD topomap adapter to project multiple explicit band fields in one request, preserving per-field frequency metadata and provenance.
- Correct shared topomap color normalization so low-magnitude PSD density fields use their actual numeric range, with explicit behavior for constant fields.
- Preserve the scalp site of validated average-reference outputs by mapping them to their positive electrode; keep bipolar outputs without a spatial rule unavailable.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/eeg-topomap`: PSD Review can request and present all five canonical band fields together while retaining exact field metadata.

## Impact

The PSD Review view model, panel, shared topomap renderer, dynamic Review request construction, backend PSD/RBP spatial projection, focused backend/WPF tests, and code-size policy for the existing shared topomap service are affected. The HTTP query and response shape remain unchanged; `field_ids` already accepts repeated values. PSD/RBP values, frequency bands, units, channel order, result identity, and window selection do not change. Existing single-band selection and frequency-bin requests remain supported. Color mapping remains presentation-only and uses each field's actual display range.
