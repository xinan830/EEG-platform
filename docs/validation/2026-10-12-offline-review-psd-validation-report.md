# Offline review PSD validation report (2026-10-12)

## Scope

This report records the verification state for `link-offline-review-psd` before
archiving it. It covers the existing WPF review/PSD integration only; it does
not validate the proposed on-demand dynamic-analysis implementation.

## Automated evidence

| Check | Result | Evidence |
|---|---|---|
| Backend test suite | **487 passed**, 2 deprecation warnings | pytest session completed in 32.94 s |
| WPF Release review/analysis filtered tests | **45/45 passed** | Release test run from the current worktree |
| WPF Release build | **Passed** | Release build completed with no errors |
| OpenSpec strict validation | **Passed** | `openspec validate link-offline-review-psd --strict` |
| Patch whitespace | **Passed** | `git diff --check` |
| Dynamic PSD short-recording baseline | **Completed** | 57 windows; source registration 83.3 ms; cold continuous preprocessing 569.7 ms; full run 1.4687 s |

The PSD baseline is a performance observation, not GUI acceptance and not a
claim that on-demand execution has been implemented. The cold preprocessing
measurement includes the current continuous offline zero-phase contract.

## Manual GUI acceptance still required

The agent environment had no running BrainPlatform WPF process or usable
desktop automation runtime, so these checks were performed by the user on the
Windows desktop with a completed local WPF recording. The user confirmed all
three passed on 2026-10-12:

1. Open a recording review, use the top `分析` menu, choose the PSD/window
   analysis entry, and confirm the right analysis panel opens without creating
   a second playback clock.
2. Change the shared high-pass/low-pass/notch values in the review toolbar,
   apply the analysis, and confirm the PSD request uses those values and the
   rendered chart updates. Confirm the waveform review remains usable while
   preparation runs.
3. Dock the analysis panel, detach it into the floating window, move/resize it,
   then close the floating window and confirm it re-docks with the same Run,
   cursor, chart result, and DataContext. Close the panel and confirm review
   playback remains usable.

This is a user-confirmed manual acceptance result; no screenshot or session
recording was attached. A successful HTTP `200`/`202` or a passing unit test
alone would not substitute for these desktop checks.

## Current conclusion

Automated checks and the three user-confirmed GUI checks passed. The old
OpenSpec change is ready to archive. The on-demand dynamic-analysis behavior is
not implemented by this change and requires a separate OpenSpec.
