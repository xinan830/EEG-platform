## Why

PSD and STFT currently share parts of the full-run window path, but their output mapping, provenance handling, and presentation assumptions are still distributed across shared code and algorithm modules. This makes a missing field in one algorithm surface as a late UI failure and makes diagnosis expensive. We need one explicit result-contract boundary so that only scientific computation remains algorithm-specific.

## What Changes

- Define a shared full-result contract for Run/artifact identity, window selection, status, failure, axes, units, and provenance.
- Require each algorithm to register a typed output descriptor declaring its scientific arrays, axes, units, time semantics, and required provenance.
- Move common structured-result validation, cursor-to-window selection, cache identity checks, and response construction behind the shared contract.
- Keep PSD spectral projection and STFT matrix projection in their algorithm modules; remove duplicated transport and compatibility logic around them.
- Add compatibility handling for older artifacts with explicit versioned rules and diagnostics; do not fabricate scientific values.
- Add contract tests that exercise PSD and STFT through the same shared path, including partial, rejected, unavailable, reused-run, malformed, and legacy artifacts.

## Capabilities

### New Capabilities

- `analysis/unified-result-contract`: Shared full-run result contract and algorithm output descriptors.

### Modified Capabilities

- `analysis/official-algorithm-foundation`: Require official algorithms that expose full-run windows to use the shared result contract while preserving algorithm-specific scientific computation.

## Impact

- Affects the desktop algorithm result contracts, full-run preview adapter and integrity checks, PSD/STFT presenters, and related tests.
- May add a versioned descriptor field to internal structured-preview handling; existing API response shapes remain compatible during migration.
- No change to PSD/STFT formulas, backend numerical units, recording data, or the on-demand scientific execution path.
- Migration is incremental: PSD and STFT become the first two adapters, then other algorithms can adopt the descriptor without adding branches to the generic executor.
