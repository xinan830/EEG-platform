# Review Playback State Notification Design

`RecordingReviewViewModel` remains the owner of the shared playback coordinator. A render tick publishes position/time and the current TNK cursor, while `IsPlaying` and `IsCompleted` notifications are emitted only when their values actually transition. Seek and session refreshes use the same value-change rule. Timeline drag state does not publish playback state because it does not change playback.

The existing waveform canvas continues to restore the complete frame on the real transition to playing. No chart special case is added; the canvas can rely on normal property-change semantics instead of inferring a transition from a redundant notification.

## Alternatives

- Debouncing or throttling chart rebinds would hide the incorrect state notification contract and could leave genuine transitions unapplied.
- Removing the start-of-playback frame restore would preserve decimated display data and change waveform behavior.

## Rollback and Testing

Rollback the view-model notification and focused regression test together. Automated tests verify that render ticks do not rebind trace data, while start/pause/end transitions remain observable. Manual playback with an active algorithm panel remains the final performance acceptance.
