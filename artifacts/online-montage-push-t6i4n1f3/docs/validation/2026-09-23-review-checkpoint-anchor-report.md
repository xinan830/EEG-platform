# Review checkpoint-anchor validation

Date: 2026-09-23

## Scope

This report covers engineering validation of exact causal checkpoint reuse for
desktop recording review. It is not clinical validation, diagnostic validation,
or proof of calibrated paper output.

## Scientific invariants

- Python remains the owner of causal filtering.
- Raw review values remain sample-major float64 in V until the explicit display
  boundary; the raw recording is never rewritten.
- Anchor identity includes recording/manifest identity, sampling rate, ordered
  source-channel schema and unit, filter settings, Python contract/checkpoint
  version, contiguous segment origin, and next sample counter.
- A recorded sample-counter gap resets the causal chain; no samples are
  fabricated to bridge it.
- A cold distant seek processes all preceding samples in the contiguous segment
  once. The optimization removes unnecessary intermediate display chunks; it
  does not claim sublinear first-pass causal filtering.

## Automated evidence

- Focused checkpoint-anchor/coordinator tests: **7 passed**.
- Desktop review-session tests after latest-only cancellation changes: **10
  passed**.
- Full desktop suite final rerun: **198 passed, 0 failed**.
- Backend checkpoint and live-filter focused suite: **33 passed, 0 failed**,
  with 2 dependency deprecation warnings.
- Full backend suite: **244 passed, 0 failed**, with the same 2 dependency
  deprecation warnings.
- Strict OpenSpec validation: **37 passed, 0 failed**.

The first concurrent full-suite invocation reproduced the repository's known
timing-sensitive live-filter handoff failure (197 passed, 1 failed); the
focused test and immediate full-suite rerun passed. No checkpoint-anchor test
failed. This is retained as flake evidence rather than hidden as a deterministic
pass.
- The new Python regression covers export/import equivalence for a 0.01 Hz
  high-pass causal stream.

## Cache and failure behavior

Anchor files are written to a disposable LocalAppData-derived cache, through a
temporary file and atomic rename. Corrupt or partial files are cache misses.
Count and byte bounds evict least-recently-used derived anchors only. The last
complete review frame remains visible on preparation failure; the UI exposes a
structured preparation failure and does not label raw data as filtered.

## Known limitation

The first cold jump into a very long contiguous segment still has latency
proportional to the required causal history. Persisted anchors and bounded idle
extension reduce subsequent seeks. A separately measured background indexing
policy would be needed to improve first-use latency without changing filter
semantics.
