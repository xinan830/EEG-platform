# Retire Legacy Metric Playback

## Why

The public `/api/playback` session still executes the stateful legacy
`EEGProcessor`, while the product viewer uses `/api/waveform-playback` and
official science uses versioned Analysis Runs. Keeping the unused metric stream
creates a second, non-Runtime scientific execution path and an obsolete public
contract. A protocol wrapper around that processor does not migrate its science.

## What Changes

- Remove creation, control, and WebSocket routes for legacy metric playback.
- Remove its session service, processor adapter, and processor-only execution
  implementation. Do not replace its metrics with waveform or Run values.
- Migrate test and diagnostic imports from redundant `eeg_core` forwarding
  facades to their canonical owners, then delete the unused facades.
- Keep waveform playback and official Analysis Runs unchanged.
- Keep historical Run/artifact readers and independent validation references;
  they are not executable playback compatibility routes.

## Impact

This intentionally breaks callers of `/api/recordings/{id}/playback` and
`/api/playback/{id}/control` or `/events`. There is no metric-stream equivalent:
use `/api/recordings/{id}/waveform-playback` for viewing and the official Run
API for scientific metrics. No stored recording or Run data is rewritten.

## Verification

Check that the retired HTTP routes are absent, the waveform route and
WebSocket still work, no production import constructs `EEGProcessor`, backend
tests and OpenSpec strict validation pass, and file-size policy remains valid.

## Rollback

The pre-removal implementation is at commit `3c4139e`. Restoring the route
requires an explicit new API decision; archived data itself needs no rollback.
