## Why

User-approved revision: standalone RBP must isolate quality by channel and retain computable artifact-bearing measurements with visible warnings. Missing/non-finite samples and invalid power remain unavailable. This revises the previous strict quality policy and advances RBP science to v4; equations and continuous filtering stay unchanged. Shared strict PSD and TNK behavior are outside this revision.

Standalone `rbp` already uses the public runtime, canonical window planner and shared compute admission, but static, full dynamic and on-demand execution duplicate reading, channel calculation and failure mapping. They can disagree on failure details and the full result currently reports clean quality even when frames fail.

## What Changes

- Use one RBP frame kernel for static, full dynamic and on-demand execution; remove duplicate paths and the unused single-channel loader.
- Preserve canonical channel order, five ratio arrays, explicit per-channel failures, and spectral/preprocessing evidence across entry points.
- Derive full-result quality from actual window outcomes and use canonical rounded sample bounds.
- Advance scientific/implementation/window-policy identities for the explicit per-channel annotation contract, preserving formulas and filtering.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analysis/on-demand-windows`: standalone RBP shares frame calculation and failure/evidence semantics with static and full execution.
- `analysis/official-rbp-faa-brainbeat-execution`: current standalone RBP v4 retains computable artifacts with per-channel warnings; filtering and equations remain fixed.

## Impact

Backend RBP module, typed spectral quality policy, scientific primitive/gateway/service, source declaration/result validator, focused tests and WPF RBP warning presentation. The public `source_shape` gains `channel_set` without weakening fixed-role checks; quality evidence gains policy and channel annotations. No new dependency, HTTP request field, endpoint or database schema. TNK, waveform and acquisition policies stay unchanged. Current arrays and single/multiple-channel requests stay supported without an old-version compatibility layer.

Scientific contract: retain 1–50 Hz continuous preprocessing, 50 Hz notch Q=30, five-band denominator and ratio units. Replace standalone RBP artifact rejection with per-channel annotation, retaining hard invalid-data rejection. Window-scoped filtering is excluded. Rollback uses the prior release with a distinct scientific/cache identity.
