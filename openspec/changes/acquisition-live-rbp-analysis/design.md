# Design

## Context

See proposal.md for motivation and specs for observable behavior. Acquisition already persists accepted batches before display/analysis, has a bounded display-filter dispatcher, and has a separate bounded live-sample access bridge backed by a bounded Python session buffer. The sample-access service is a source boundary, not an algorithm executor. Offline RBP currently consumes recording-backed spectra using continuous-before-window-slice preprocessing, ordered channels, a 10-second dynamic default, and per-channel quality annotation. These existing paths are not interchangeable.

## Goals / Non-Goals

**Goals:** Add a session-scoped live RBP execution path during preview and recording; keep all scientific computation in Python; keep capture, display filtering, and offline backtrace independent; expose only current, quality-aware results to the acquisition UI.

**Non-Goals:** Change offline RBP, AnalysisRun scheduling or persistence; add other algorithms or stateful BrainBeat; save live results to a project; change raw chunks, recording review, or display-filter semantics.

## Decisions

1. **Use an independent analysis branch after accepted raw persistence.** The coordinator continues to write raw data first, then fans accepted samples to display and to a distinct live-analysis queue. Reuse the existing bounded live-sample access session and its identity/gap-aware sample reads where they satisfy this contract, but add a separate algorithm executor and result ownership. Do not extend the display-filter worker into a general algorithm worker. Alternatives considered: calculate in WPF (breaks scientific ownership), or treat display-filter output as RBP input (changes science when the user changes the view).

2. **Keep all RBP calculation and preprocessing in the backend.** Adapt the current RBP calculation at its algorithm boundary to consume a live session sample source; keep the five-band integration and existing channel quality rules in backend-owned code. The desktop sends selected channel names and displays typed results. It must not duplicate FFT/filter/power formulas. Live execution needs an explicit implementation identity and evidence for sample bounds, units, channel order, preprocessing scope/settings, quality and failure reason.

3. **Validate preprocessing equivalence before claiming the offline identity.** Current RBP declares continuous 1–50 Hz preprocessing before slicing, a 50 Hz notch Q=30 by default, and a 10-second dynamic window. Live streaming has a finite history and evolving filter state. Before implementation claims `official-rbp-v4`, compare live outputs against the existing offline path on deterministic contiguous recordings, including startup warm-up, window boundaries and supported notch settings. If numerical and quality semantics do not match, expose the live path under a distinct explicit identity; do not change offline RBP or its version to force equivalence. The initial release can still be engineering validation of the live path, not clinical validation.

4. **Use actual samples and canonical sample coordinates.** Windows are aligned from the acquisition session's sample counter and actual sampling rate, not UI timers or HTTP arrival time. Select EEG channels explicitly and preserve the selected order; exclude counter/trigger channels. A gap, reset, overlap or buffer overwrite invalidates windows that cross it. No interpolation or zero-fill is permitted; resume only after the minimum valid contiguous window and preprocessing warm-up are available. Use the manifest's current RBP defaults unless explicit live controls are added in a later change.

   A montage's sample boundary coordinates when subsequent acquisition batches use the selected revision; it does not discard earlier raw samples from the bounded analysis source. After a switch, the live sample source applies the new montage consistently to the latest complete retained window, preserving its original sample counters. This is valid because analysis retains raw source channels and projection is derived from one immutable montage snapshot per request. Actual gaps and overwritten samples remain unavailable.

5. **Keep output ephemeral and bounded.** The backend retains only the bounded input needed for the live window plus the latest session result; the desktop shows the latest result and status. Session close/cancellation clears worker state, samples and result. No database records or offline Run APIs are involved. Overload invalidates affected windows and reports a structured state instead of silently calculating from skipped input.

6. **Separate lifecycles and failure reporting.** Each acquisition session owns its live-analysis session, cancellation and result identity. Backend transport/worker failures update live analysis status only; they cannot fault the acquisition lifecycle or roll back raw writes. The result boundary uses explicit message types and stable error codes. If the local HTTP boundary is used, version the request/result messages and preserve structured failures; do not add a public offline endpoint contract.

7. **Coalesce small device callbacks before live HTTP transport.** The live-access dispatcher reuses the existing 50 ms fixed-duration aggregator so each HTTP upload carries a contiguous block instead of one request per vendor callback. A sample-counter gap or session change flushes the preceding partial block before the new range; stopping flushes the tail before closing the backend session. This bounds request overhead without changing capture, raw persistence, or sample coordinates.

8. **Reuse review presentation without review execution state.** The acquisition panel uses the shared `AnalysisDockHost`, `AnalysisPanelShell`, and `RbpChart`; its view model projects only current-session backend windows into the chart's existing history model. It does not construct or call `ReviewAnalysisCoordinator`, and stale session, revision, and queued UI callbacks are discarded.

9. **Fence results by montage revision, not window start.** A reprojected rolling window may begin before the montage switch boundary while every sample is intentionally projected through the new montage. The `live-analysis-update-v2` envelope carries the montage revision captured when the algorithm session starts; the desktop accepts a result only when that revision matches the active montage, so earlier window coordinates remain valid and delayed old-montage work cannot replace them. The backend and its sole desktop consumer upgrade together without a v1 response fallback.

## Risks / Trade-offs

- [Live preprocessing may not match the offline whole-recording filter behavior, especially during startup and after gaps] → Run deterministic equivalence tests first; show live identity and processing scope; never imply offline equality without evidence.
- [Live sample retention can overwrite a window before execution finishes] → Bound execution latency and queue capacity; invalidate overwritten windows and surface unavailable/overload states.
- [A streaming transport failure can create hidden discontinuities] → Carry session ID and sample-counter ranges end to end; treat lost, duplicate, or reordered ranges as explicit invalid windows.
- [Preview and recording have different storage lifetimes] → Keep algorithm source access tied to the acquisition session and close it on stop, fault, cancellation, or app shutdown regardless of raw-writer state.

## Migration Plan

No data migration is needed. Add the live path behind the acquisition UI's opt-in control and retain existing preview, recording, display filtering, offline RBP and review paths. Rollback is to disable/remove the live-analysis control and worker; raw recordings and saved Runs remain readable through the unchanged current workflows.

## Validation Strategy

- Backend contract tests: channel filtering/order, V/float64 layout, sample-counter alignment, gaps/overlap/reset, Nyquist rejection, session isolation, bounded overwrite and structured failures.
- Scientific tests: compare live and offline RBP on deterministic signals at aligned windows; cover startup, steady state, gap recovery, notch modes, finite artifact-bearing segments, missing/non-finite samples, all-channel failure, units, and channel order. Record any non-equivalence in the live implementation identity and evidence.
- Desktop tests: preview and recording activation; independent queue overload/worker failure; raw persistence and display continue; results from stale sessions ignored; close/fault clears temporary results; no result is written to offline analysis or backtrace.
- Integration check with a simulated acquisition source before hardware validation; then verify actual device channel metadata and sample-counter behavior on the ANT adapter. Engineering tests do not establish clinical validity.

## Implementation Validation (2026-11-20)

- Desktop acquisition tests: 200 passed, including live bridge upload/projection, close/re-enable, in-flight disable ordering, stale session/revision rejection, overload, and transport failure.
- Backend live sample-access and RBP tests: 14 passed.
- The full desktop suite ran 1,427 tests: 1,419 passed and 8 failed in review chart/view-model, montage reconciliation, optional unconfigured HTTP acceptance, and Windows symbolic-link privilege cases. These failures are outside the changed acquisition paths; the focused acquisition suite passed.
- ANT hardware validation remains outstanding; no hardware or clinical validation is claimed.
