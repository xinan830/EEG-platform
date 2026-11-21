# Tasks

## 1. Live algorithm contracts

- [x] 1.1 Add typed window/config/result-envelope contracts and a small algorithm registry; verify duplicate registration, unknown algorithms, and strict envelope fields with focused contract tests.
- [x] 1.2 Wrap the current RBP calculation as the first window adapter without changing its formulas; compare deterministic input windows before and after the adapter for equal values, units, quality, provenance, identity, and sample bounds.

## 2. Common session coordination

- [x] 2.1 Move acquisition-session/algorithm lifecycle, sample-aligned endpoint scheduling, latest revision, cancellation, and close ownership into a common coordinator; verify windowed algorithms share scheduling without reading across gaps or overwritten ranges.
- [x] 2.2 Add backend tests for variable transport batches, session replacement, gaps and recovery, bounded overload, reversed result completion, and isolated per-algorithm failure.

## 3. Generic live transport

- [x] 3.1 Replace RBP-only live start/stop/update messages with a versioned algorithm-neutral envelope while preserving RBP payload fields; verify HTTP route tests and desktop bridge tests cover start, stop, stale-session rejection, and typed failures.
- [x] 3.2 Route live updates by algorithm identity outside acquisition lifecycle code and keep the current RBP presenter/chart unchanged; verify RBP updates still render and unknown algorithm payloads cannot corrupt acquisition state.

## 4. Integration acceptance

- [x] 4.1 Run simulated acquisition stream tests for multiple windowed adapters, gaps, overload, result ordering, and session shutdown; record the accepted common contract and confirm no offline Run or review coordinator is used.
- [x] 4.2 Run backend and desktop acquisition test suites, strict OpenSpec validation, and diff checks; verify existing RBP live outputs and offline review tests remain unchanged. Evidence and reproduced baseline failures: docs/validation/2026-11-20-live-analysis-coordination.md.

## 5. Live RBP rolling startup

- [x] 5.1 Define regression cases for 23-second activation (`[13s, 23s)` immediately, then `[14s, 24s)`), early activation at 4 seconds (`[0s, 10s)`), and stable session-relative timestamps after retention overwrite with non-zero device counters.
- [x] 5.2 Keep the existing bounded sample transport active from the first preview/recording batch. Verify sample order, V units, capacity for the configured sampling rate, enable/disable without clearing history, close/fault cleanup, and isolation from device reading, write and waveform paths.
- [x] 5.3 Track the first accepted sample per source and schedule the latest window on enable with the existing worker limits. Cover absent history, gaps and recovery, overwritten reads, overload, stale revisions and session replacement.
- [x] 5.4 Verify first-result delivery through the desktop bridge, including result delivery when activation is allowed without a fresh batch, one-second cadence, chart timestamps, and disabling or stopping while a result is pending. Reuse the existing RBP result reader and chart. Run the shared RBP numerical regression on identical spectra.
- [ ] 5.5 Run focused backend and desktop acquisition suites, verify strict OpenSpec and file-size checks, and record results. On hardware, compare RBP off/on/reenabled during a continuous recording, check the 23-second acceptance case and confirm capture counters and saved-file replay under the added idle transport load. Automated evidence is in `docs/validation/2026-11-20-live-analysis-coordination.md`. Hardware verification remains open.
