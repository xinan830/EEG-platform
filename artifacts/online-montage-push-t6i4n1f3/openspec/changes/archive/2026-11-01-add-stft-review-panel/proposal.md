## Why

Recording Review offers RBP but cannot display the existing standalone STFT result alongside waveform playback. STFT should consume the same recording and cursor without a second reader or playback clock.

## What Changes

- Add a selectable STFT panel with a channel selector and compact heatmap that accumulates visited windows along recording time.
- Generalize the existing declared dynamic-window Review adapter and panel lifecycle for RBP and STFT. Only one panel is active; switching/closing releases its subscription and rejects late results.
- Reuse current bounded STFT execution, parser, units, quality and provenance. No full Run or whole-record preprocessing fallback.

## Capabilities

### New Capabilities
- `desktop/stft-review-panel`: STFT panel composition and lifecycle in recording Review.

### Modified Capabilities

None. This extends the accepted Review panel set beyond the earlier RBP-only change; its science and waveform behavior remain unchanged.

## Impact

Desktop Review composition, shared dynamic adapter, STFT presentation and focused tests. No backend HTTP schema, storage or numerical contract change. Migrate all internal callers of the RBP-specific adapter to the shared adapter, with no compatibility wrapper. Non-goals: concurrent panels, new STFT formulas, full-record execution, TNK integration, live acquisition or unrelated waveform performance work.
