# Design

## Ownership

`app.api.playback` continues to own only waveform playback routes.
`app.services.waveform_playback` remains the viewer's playback service.
Official algorithms remain under `app.algorithms` and are executed through
`app.algorithm_runtime` as Analysis Runs. The obsolete metric-stream service
is removed, not reimplemented in a service or silently redirected to Run
science with different windows, filters, units, and quality states.

## Compatibility decision

This is an intentional API retirement approved by the user. The web client
uses only waveform playback; no repository product caller uses the retired
routes. Unknown external clients will receive 404 after deployment. Existing
recordings, definitions, Runs, summaries, and artifacts remain readable.

Pure scientific helpers used by independent validation stay reference-only;
their existence is not a product playback endpoint. The non-runnable
BrainBeat catalog descriptor remains a historical scientific identity, not
a promise that the retired metric stream still runs.

Redundant import facades with only test/diagnostic callers are removed after
those callers import canonical algorithm and scientific owners. Independent
reference formulas and scalar Definition preview remain; neither is a
forwarding facade for an active product path.

## Invariants

- Recording-relative sample/time coordinates and waveform V-to-uV conversion
  do not change.
- No IAPF, RBP, BrainBeat, or other legacy metric payload is fabricated by
  waveform playback.
- No new scientific formula or filter behavior is introduced.
- Removed processor-only tests are replaced by route-absence and architecture
  checks; waveform and official Run suites remain the positive regression gate.
