# Dynamic spectrogram playback mode

## Changes

- Added static/dynamic mode selection to the spectrogram panel.
- Dynamic mode uses a recent 5/10/20-second window and refreshes at a selectable 1/2/5-second cadence.
- Dynamic requests are serialized; if a request is still running, the newest refresh is queued instead of creating concurrent requests.
- Playback position drives the dynamic window; pause stops the timer and preserves the last rendered image.
- The previous image remains visible while a refresh is pending or fails.
- The configured spectrogram API remains the sole source of calculations; the frontend only renders returned power matrices.

## Verification

- Backend: 92 tests passed.
- Frontend tests and `vue-tsc --noEmit` passed.
