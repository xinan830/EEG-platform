## 1. Scheduling and source ownership

- [x] 1.1 Add regression coverage for non-publishing preparation, bounded lookahead, missed deadlines and cancellation.
- [x] 1.2 Separate preparation from validated binding and add bounded paired playback preparation.

## 2. Projection and lifecycle integration

- [x] 2.1 Reuse prepared backend projections in both numeric panels with range/window guards.
- [x] 2.2 Wire playback, pause, seek, drag and close cancellation and add preparation/publication diagnostics.

## 3. Validation

- [x] 3.1 Run desktop regression tests/build, relevant backend scientific tests, strict OpenSpec and diff/size checks.
- [x] 3.2 Document measured automated evidence and outstanding real-recording cold-cache acceptance.

Evidence: `docs/validation/2026-11-07-cold-spectral-playback-lag.md`. Desktop 420/422 passed (two failures reproduced against baseline), 12 new cases passed, backend 51 passed. Real-recording cold-cache GUI acceptance remains outstanding.

## 4. FAA/IAPF follow-up root causes

- [x] 4.1 Add cross-context FAA numerical/source-reuse and scalar playback lifecycle regressions; verify terminal delivery is independent from cleanup.
- [x] 4.2 Share continuous preprocessing ownership with FAA, preserving pair order, identity, filtering, quality and resource limits.
- [x] 4.3 Reuse the two-target preparation queue for FAA/IAPF and integrate pause, seek, drag, configuration and disposal fencing; decouple terminal cleanup.
- [x] 4.4 Run backend parity/source tests, desktop playback/protocol regressions and build, strict OpenSpec/diff/size checks; record measured evidence and manual acceptance limits.

Follow-up evidence: `docs/validation/2026-11-07-faa-iapf-playback-lag.md`. Backend 192 passed; desktop 511/513 passed, including all 16 new cases. The two existing baseline failures remain explicit. Build has zero errors; strict OpenSpec, diff and changed-file size checks passed. A subsequent user 1X recording experiment confirms 41/41 prepared playback hits and 47 newly computed backend windows, with one cold continuous source per algorithm. Long-duration/2X and complete GUI acceptance remain outstanding.
