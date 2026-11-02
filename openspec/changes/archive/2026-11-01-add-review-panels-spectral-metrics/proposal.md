## Why

Recording Review currently exposes RBP and STFT even though FAA, IAPF, Theta/Beta, and Brainbeat already have declared backend dynamic-window result contracts. The Review binding still branches directly between RBP and STFT, so adding these algorithms by copying page-specific orchestration would fragment the shared cursor, identity, cancellation, and stale-response rules.

## What Changes

- Add selectable Review panels for FAA, IAPF, Theta/Beta, and Brainbeat, using the active recording and Review cursor with the existing on-demand dynamic execution path.
- Replace algorithm-specific RBP/STFT lifecycle branching in Review composition with a registration boundary for parameter configuration and result presentation.
- Keep each algorithm's input mapping, output field, unit, quality/provenance validation, and scalar chart projection in its owning algorithm module.
- Retain one active Review algorithm panel; switching or closing it releases its subscription and clears its presentation while waveform playback continues.

## Capabilities

### New Capabilities
- `desktop/review-scalar-algorithm-panels`: Algorithm-owned scalar dynamic panels in Review for FAA, IAPF, Theta/Beta, and Brainbeat.

### Modified Capabilities

None.

## Impact

- Affects WPF Review composition, algorithm-specific Review configuration/presentation adapters, compact scalar trend charting, and focused desktop tests.
- No backend formulas, scientific versions, preprocessing contracts, thresholds, cache identities, HTTP schemas, or persistence formats change. FAA and Brainbeat retain their current paired-source and preprocessing behavior.
- Existing RBP and STFT Review panels remain supported through the same lifecycle boundary. No compatibility adapter or duplicate full-Run path is added.
- Non-goals: simultaneous panels, changing numeric results or policy windows, changing full-run detail workspaces, or moving scientific calculation into WPF.
- Migration: no stored data or API migration. Unsupported/missing role mappings remain explicit unavailable configuration rather than channel substitution.
