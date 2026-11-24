# Dynamic Spectral Series Validation Report

## Implemented scope

- PSD dynamic output: `window x frequency`, linear unit `V^2/Hz`.
- STFT dynamic output: `window x inner_time x frequency`, linear unit
  `V^2/Hz`, display unit `dB re 1 uV^2/Hz`.
- Window coordinates are recording-relative half-open samples, with seconds
  retained as display metadata.
- Full window evidence is stored in the immutable artifact as JSON; Run
  summaries retain bounded state counts, shapes, units, axes, and provenance.

## Verification

- Backend suite: `286 passed`.
- Dynamic PSD/STFT Run artifact tests passed.
- One-window dynamic/static equivalence passed for PSD and STFT using a single
  complete `[20, 30] s` window.
- Gap/quality rejection preserves `NaN` rows and structured reasons.
- OpenSpec strict validation passed.
- `git diff --check` passed.

## Rollback point

The pre-implementation plan commit is `b4f63f1`.
