# Design

## Context

See proposal.md for motivation. Backend remains the only scientific processor. WPF owns confirmed configuration and display; raw recordings remain immutable.

## Goals / Non-Goals

Restore the seven reproduced interaction and presentation contracts and four follow-up findings without changing scientific formulas or taking ownership of unrelated Review failures. Keep recording time in seconds, EEG traces in microvolts and ICA display activations explicitly dimensionless.

## Decisions

- Keep pending inspection and its configuration revision separate from the confirmed ICA tuple. Confirm validates the pending fingerprint and selected component indices. Reset and upstream changes invalidate in-flight work. Replacing only a fingerprint is rejected.
- Use one preprocessing configuration revision and independent operation generations; cancellation alone is insufficient when a transport returns after cancellation. Extract the preview lifecycle as a partial owner only if required for coherent responsibilities and file size.
- A preview is cleared before replacement and accepted only while its request, configuration and viewport remain current. Montage changes invalidate ICA and inspection visuals. Playback hides fixed previews.
- Show selected bad-channel policies as pending unless a matching successful backend preview confirms treatment. Never restyle original samples as repaired.
- Return per-channel extrema/gap display coordinates with a declared envelope method, reusing the project's finite-run/min-max approach. This is a display envelope, not a resampled scientific time series.
- Resolve only raw/REF/AVG electrode sites for ICA scalp maps; reject bipolar and unsupported derived outputs.
- Keep bounded inspection limits but label actual returned coverage; explicitly display that STFT/TNK omit ICA/SSP/EOG removal.

## Risks / Trade-offs

- Inspection reports are visual state; only changes to selected bad inputs or an automatically enabled rejection policy invalidate ICA and scientific previews. An untouched ICA area retains its initial status.
- Temporary waveform montages suspend previews before asynchronous display transitions; returning to an analysis-compatible frame resumes previews without invalidating confirmed ICA. Playback and temporary montage suspension are independent.
- Interpolated output metadata follows positive and negative/reference dependencies from provenance, never a derived-label prefix.
- Share finite-run extrema index selection between microvolt previews and dimensionless normalized ICA activations. ICA cards carry explicit recording seconds from sample zero and null gaps; WPF uses those seconds against the full snippet duration, not uniform point indices. Finite ICA projections are scattered back into their original sample slots because projection is pointwise. No scientific source arrays or ICA fitting formulas change.

Display envelopes are not frequency-analysis input; preserve science arrays and document compression. Preview/API contract changes require a coordinated WPF update. Rejecting stale ICA work may require a new review after user changes inputs; clear status makes this intentional. Existing independent Review failures are recorded separately.

## Migration Plan

No persistence migration. Update backend preview response and desktop DTO/consumer together. Roll back these changes together. Verify targeted concurrency and rendering tests, backend APIs, Release build, OpenSpec and modified-file size policy.
