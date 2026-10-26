# Brain Platform Handover

Updated: 2026-10-26 (Asia/Shanghai)

This note is for a new session with no chat context. Treat repository source and the current OpenSpec artifacts as authoritative; this file summarizes the present troubleshooting state and must not be read as proof that the latest UI has been manually accepted.

## Current Task

The user is fixing bugs in the TNK dynamic-analysis workbench one at a time. The immediate unresolved report is from the latest screenshot: the BrainBeat line has a value (`0.31 ratio`), while BrainBeat Flat is present in the legend but reads `当前窗口不可用`; the chart appears to show only one curve. The user said one value did not appear. Do not infer that the whole algorithm or recording is missing data.

The user also clarified the RBP time-axis intent in the previous issue: labels should be about every 10 seconds at the default view and become denser, down to every second, when zoomed in.

## What Was Completed Earlier

- RBP stacked chart display was adjusted after screenshots showed nonzero backend percentages but degenerate red-only Fz/F4 plots. The current source updates stacked band series together and reapplies the explicit 0-100 percent Y range after `ZoomExtents()`. Time ticks were returned to SciChart adaptive ticks. These changes have focused tests and strict OpenSpec validation recorded in change `migrate-tnk-biofeedback-algorithms`, task 5.14.
- On-demand score-state display was added for the four Composite Meditation score bands: `<55`, `55-65`, `65-75`, `>75`. The UI projection uses returned composite score points and their recording timestamps, breaks accumulation at unavailable points, and refreshes when owned dynamic windows complete. The focused `TnkWorkbenchTests` were reported passing 13/13; OpenSpec strict validation and `git diff --check` passed at that point.
- These are source/test validations, not visual acceptance on a real recording. The project change still has unrelated open tasks for independent scientific parity (5.2), manual WPF acceptance (5.4), and final project validation/docs (5.5). Do not mark the whole change complete.

## Current Diagnosis: BrainBeat Flat

Confirmed source facts:

- `backend/app/algorithms/tnk/group.py` orders `tnk_aperiodic` before `tnk_brainbeat_flat` in `_CHILD_ORDER`.
- Dynamic BrainBeat Flat follows the ordinary 4-second window / 1-second frame cadence, while IAPF, HAI, and aperiodic use a 30-second rolling window and 5-second cadence (`_dynamic_child_range`).
- When a child is not due, `_execute_children` inserts a scheduled placeholder into `first_result` with no fitted offsets/exponents.
- When executing BrainBeat Flat, `_execute_children` reads only `first_result["tnk_aperiodic"]` from the current frame and copies its fit evidence into `aperiodic_fit_by_role`. It does not visibly retrieve the most recent earlier valid aperiodic fit.
- `BrainbeatFlatModule.compute` returns `TNK_FIT_INVALID`/unavailable when either required role has no fit evidence.
- The latest screenshot confirms BrainBeat has data (`0.31 ratio`) while BrainBeat Flat does not have a current value.

High-confidence hypothesis, not yet confirmed from the actual response: on Flat frames where aperiodic is not due, the scheduled placeholder supplies no fit, so Flat returns unavailable despite having a valid earlier fit. The dependency may also be absent on a due frame if the fit itself is unavailable or rejected. Obtain the actual `tnk_aperiodic` and `tnk_brainbeat_flat` child rows, quality, failure code, actual ranges, and fit provenance from the same window before selecting a fix.

Important design decision remains open: should Flat reuse the most recent valid aperiodic fit, and if so, how should its actual fit time/config/quality be represented in provenance and when must it be invalidated? Do not silently copy a stale fit or change scientific scheduling without an explicit tested contract. Avoid WPF-side formula or scientific recomputation.

Likely files to inspect:

- `backend/app/algorithms/tnk/group.py` (`_execute_children`, `_dynamic_child_range`, fit evidence transfer)
- `backend/app/algorithms/tnk/stateful.py` (`BrainbeatFlatModule.compute`)
- `backend/tests/test_tnk_group.py`, `backend/tests/test_tnk_modules.py`, `backend/tests/test_tnk_strategy_parity.py`
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/TnkResultChartProjection.cs` (backend result projection only)
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/TnkWindowChartHistory.cs` (on-demand chart history)
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/TnkWorkbenchPresentation.cs` (tab series selection)

## Current Workspace State And Cautions

- The repository worktree is heavily modified and contains user work. Never reset, checkout, or clean unrelated files. Inspect exact diffs before touching overlapping files.
- `HANDOFE.md` was absent/deleted at the beginning of this handoff request; this document has now been recreated at the user's explicit request. Check git status before assuming it is tracked.
- Prior targeted desktop test/build attempts were affected by a running `BrainPlatform.Desktop.exe` locking the apphost. At handoff time, process PID 49820 was observed running from `desktop-client/BrainPlatform.Desktop/bin/Debug/net10.0-windows/BrainPlatform.Desktop.exe`. Confirm current PID before building. Do not kill a newly running user process without checking; earlier permission to close the desktop app was given, but the latest owner is uncertain.
- There are legacy-package compatibility warnings (NU1701) and an SDK preview notice. Record them but distinguish warnings from failures.
- A previous assistant briefly tried score-duration accumulation in WPF, which conflicts with the project's rule that state-duration statistics are backend-owned. Review the current uncommitted `TnkOnDemandScoreStateProjection.cs` and `TnkWorkbenchSidebar.cs` changes carefully. If retaining this behavior, move authoritative calculation to the backend and keep WPF as a renderer; alternatively clearly scope the feature if existing project architecture explicitly permits display projection. Do not repeat the earlier claim that backend calculation was implemented unless source proves it.
- The last test command for the score percentage assertion initially failed due exact floating-point comparison; the assertion was changed to round to 8 decimals. A later no-build run reported 13/13, but ensure a clean current build/test after resolving the running app lock.

## Next Steps

1. Read `AGENTS.md`, `docs/工程开发契约.md`, the brain-research-platform skill's `wpf-algorithm-acceptance.md`, and OpenSpec task instructions for `migrate-tnk-biofeedback-algorithms` before code edits.
2. Check git status and inspect diffs for `group.py`, the score-state view-model/helper, RBP plot, tests, and current OpenSpec. Preserve unrelated pre-existing edits.
3. Capture or inspect the actual on-demand window response for a time where BrainBeat has a value but Flat is unavailable. Compare child quality/failure, actual ranges, `aperiodic_fit_by_role`, and the recording cursor with the screenshot.
4. Confirm the intended scientific dependency semantics from `docs/algorithms/tnk-source-ledger.md` and the relevant OpenSpec design/spec. If reuse of a prior fit is not defined, update the change artifacts before implementing it.
5. Add backend regression tests for due and not-due fit frames, missing/rejected fits, cache/context identity, and provenance. Keep frontend tests focused on rendering the returned value or explicit unavailable reason.
6. Rebuild/test with no EXE lock; run focused backend and desktop tests, `openspec validate migrate-tnk-biofeedback-algorithms --strict`, and `git diff --check`. Then reopen the desktop client and manually verify the same recording/window.
7. Report facts, hypothesis, tests, and remaining real-data UI acceptance separately. Do not mark outstanding OpenSpec tasks complete without their required evidence.

## Project Invariants

- Scientific calculations and quality decisions belong to the FastAPI backend; WPF binds returned values and formats display only.
- Times are recording-relative seconds unless an explicit contract says otherwise. Preserve actual window ranges and channel role/source mapping.
- Missing/rejected results remain null/gaps with structured reasons; never fill them with zero or interpolate.
- The TNK group exposes dynamic analysis; each child retains independent quality, failure, state, and provenance.
- Check the 400-line soft target and existing `docs/code-size-policy.json` before adding substantial code.
- OpenSpec current contract files, not old chat statements or stale docs, govern behavior.
