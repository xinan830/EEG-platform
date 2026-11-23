# Proposal

## Why
Users need a complete temporary waveform montage. The previous single appended bipolar trace misunderstood the requested full-profile workflow.

## What Changes
- 在线导联 opens the existing full 导联设置 editor using current-session sources, reference controls, names, colors and output ordering.
- Saving adds the named profile to 显示导联 and selects it automatically, replacing all waveform outputs. Users can switch back to ordinary profiles.
- Profiles remain session-only; recording and algorithm inputs do not change. Remove the superseded simplified dialog and appended trace.

## Capabilities
### New Capabilities
- `desktop/temporary-waveform-montage`: Complete session-scoped waveform montage configuration and selection isolated from analysis and recording.
### Modified Capabilities
- None.

## Impact
Desktop montage editor, acquisition and review selections, waveform projection/cache and tests. No backend, HTTP/WebSocket, dependency, storage schema or scientific formula changes. Existing V-valued display projection preserves sample counters, absolute seconds, gaps and source arrays. Outputs use configured order and colors. Invalid mappings reject activation visibly.
