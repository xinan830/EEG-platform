# Add PSD Band Share Chart

## Why

PSD already computes absolute and relative power for the standard EEG bands,
but the official structured result exposes only the frequency-series array.
The WPF detail page therefore cannot render a traceable band-share view without
recomputing science in the client.

## Scope

- Expose the five standard band shares as a formal PSD structured array.
- Add a separate WPF band-share chart component and view-model surface.
- Support static values and the selected complete dynamic window.

## Non-goals

- No client-side PSD, integration, or normalization.
- No replacement of the existing PSD spectrum chart.
