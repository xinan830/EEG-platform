# Proposal

## Why

Review preprocessing confuses configured treatment with displayed results, changes confirmed ICA state before confirmation, and retains stale asynchronous previews. Display sampling can hide gaps, bipolar channels acquire false scalp positions, and partial scans are described as whole-recording inspections.

## What Changes

- Separate pending ICA inspection from confirmed models; invalidate work when scientific inputs change.
- Publish previews only for the current configuration and viewport; clear obsolete data on failure, reset and playback.
- Distinguish selected treatment from verified processed traces, preserve extrema and gaps, reject unsupported bipolar scalp placement.
- Show actual inspection coverage and bounded STFT/TNK artifact-removal limitations.
- Preserve confirmed ICA on unchanged rescans, suspend overlays for temporary display montages, derive interpolation badges from formula dependencies, and preserve ICA waveform extrema with explicit coordinates.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/recording-review`: Reliable preprocessing confirmation, preview identity, scientific presentation and inspection coverage.

## Impact

Affected: Review view models, controls, montage integration, backend previews and tests. The preview API replaces its shared times_s with per-channel trace_times_s and explicit display compression metadata; producer and WPF consumer must update together, with no fallback to obsolete uniform sampling. Source recordings, V/float64 matrices, channel order, absolute sample time and algorithm formulas remain unchanged. No migration or dependency is required. Rollback restores the producer and consumer together. Unrelated review failures and hardware behavior are outside scope.

ICA component previews add preview_times_s, preview_unit=z-score and preview_compression; preview_waveform allows null gaps. Desktop validates this contract and rejects nonempty waveforms without matching coordinates/units. Deploy or roll back backend and desktop together.
