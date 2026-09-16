## Why

RBP, FAA and BrainBeat appear in the official algorithm catalog but currently
remain shadow-only. This leaves users unable to run three documented platform
algorithms through the same traceable Run, artifact, debug-evidence and
playback-result path used by IAPF and Theta/Beta.

## What Changes

- Register executable backend modules for official RBP, FAA and BrainBeat.
- Make the catalog advertise each algorithm only after its module, input
  validation, quality behavior, serialized evidence and regression tests are
  present.
- Add client-safe parameter schemas and result shapes: RBP returns the four
  band shares, FAA records explicit F3/F4 source channels, and BrainBeat
  records explicit Fz/Pz source channels and IAPF evidence.
- Extend the read-only frontend result card to render backend-provided
  multi-band RBP values without recomputing them.

## Capabilities

### New Capabilities

- `analysis/official-rbp-faa-brainbeat-execution`: Traceable static and
  dynamic execution contracts for the three remaining official algorithms.

### Modified Capabilities

- `analysis/official-algorithm-execution`: Expand executable official
  algorithms beyond IAPF and Theta/Beta while preserving algorithm-specific
  time, channel, unit and quality contracts.
- `analysis/official-algorithm-catalog`: Mark RBP, FAA and BrainBeat runnable
  only when their runtime modules can supply client-safe parameters and result
  contracts.

## Impact

Affected areas are the algorithm-runtime registry, official Run request
schema, Run executor/serializer, official catalog, the waveform-and-algorithm
workspace, result rendering, tests and algorithm documentation. Existing
spectrum, spectrogram, historical Run and raw-file APIs remain unchanged.

## Non-Goals

- No change to frozen RBP frequency boundaries or integration rules.
- No channel alias or positional inference: F3/F4 and Fz/Pz sources remain
  explicit user selections.
- No clinical conclusion, realtime hardware support or arbitrary Python
  extension execution.
- No claim that the offline BrainBeat Run is numerically interchangeable with
  the existing stateful realtime processor.
