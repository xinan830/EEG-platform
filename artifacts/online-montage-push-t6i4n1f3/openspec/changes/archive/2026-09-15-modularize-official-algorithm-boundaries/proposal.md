## Why

Official EEG algorithms currently have their scientific identity split across definitions, legacy implementation files, shadow checks, and a frontend name map. That split makes availability ambiguous and allowed the user-facing algorithm picker to hide installed official algorithms.

## What Changes

- Introduce one code-owned official-algorithm registry for RBP, Theta/Beta, FAA, BrainBeat, and IAPF.
- Expose a read-only official catalog API, including the installed Definition/version identity and authoritative availability.
- Move official calculation entry points behind per-algorithm modules while retaining legacy import facades.
- Make the waveform algorithm picker render the official catalog returned by the backend; it must not infer availability from English names, graph fields, or quality rules.
- Keep all five algorithms in `shadow_validation` and non-runnable during this change.

## Capabilities

### New Capabilities

- `analysis/official-algorithm-catalog`: A read-only, backend-authoritative catalog of installed official EEG algorithms and their engineering-validation availability.

### Modified Capabilities

- `analysis/algorithm-definitions`: Official execution metadata is derived from the official registry instead of an independent API-local mapping.

## Impact

Changes backend module ownership, adds `GET /api/official-algorithms`, updates algorithm capability output and the waveform algorithm picker. No migration, dependency, EEG math, unit, timing, Definition API removal, or user-result change is introduced.
