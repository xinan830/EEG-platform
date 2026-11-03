# Fix Review Playback State Notifications

## Why

Review reports `IsPlaying` from every render-clock tick even when playback remains in the same state. The waveform canvas treats that property notification as a transition and rebuilds every trace, creating repeated chart work that competes with algorithm result presentation.

## What Changes

- Publish playback-state properties only when the playback coordinator changes their values.
- Keep cursor publication and TNK cursor updates on every advancing playback tick.
- Preserve the one-time full waveform restore when playback actually starts.

## Compatibility Impact

No public API, stored data, algorithm request, or recording format changes. WPF observers stop receiving redundant same-value `IsPlaying` and `IsCompleted` notifications.

## Scientific Contract Impact

No signal samples, timestamps, units, filters, channels, algorithm windows, or scientific results change.

## Non-Goals

- No chart-library replacement, playback throttling, data decimation change, or algorithm scheduling change.
- No attempt to fix unrelated UI dispatcher or rendering costs.

## Migration

No persisted-state migration is required. Rebuild the desktop client and validate playback with Review algorithms enabled.
