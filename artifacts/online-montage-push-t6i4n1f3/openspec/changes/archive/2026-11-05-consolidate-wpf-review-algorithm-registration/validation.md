# WPF Review registration validation — 2026-11-05

## Scope and result

Nine existing Review algorithms retain their own registered adapter/configuration/result route. MainWindow now injects the algorithm client instead of maintaining a separate adapter list. Panel title, scalar binding and strongly typed XAML template selection use the active registration. Parameter subscriptions are installed and released from the registered configurations.

Only desktop source/tests, architecture/size documentation and this OpenSpec change were edited by this task. Backend changes appeared concurrently in the shared workspace; this task did not edit, revert or validate them. Backend API contracts, scientific formulas, default parameters and numeric result fixtures were not changed by this task.

## Executed checks

- Desktop build, including WPF/BAML: passed through the targeted `dotnet test` build.
- Review namespace tests excluding the two known failing test methods: **311 passed, 0 failed**. The exclusion omits three cases because the drag method has two parameter values.
- New registration/active scalar command/XAML coverage tests: **7 passed, 0 failed**, rerun after strengthening named-command and template mapping assertions.
- New coverage checks all nine adapter IDs, PSD/RBP topomap capabilities, TNK event capability, rejection of a mismatched adapter, five scalar command bindings, and both menu/toolbar entry surfaces with all five explicit template kinds.
- Strict validation: `openspec validate consolidate-wpf-review-algorithm-registration --strict` passed.
- Scoped `git diff --check` and touched-source file-size ledger checks passed. MainWindow is 550 lines (previously 562); its stale 540-line ledger is corrected. Review main ViewModel is 703 lines (previously 727), below its 730-line reviewed maximum, retaining its existing split plan. New registration factory is 67 lines.

## Existing failures independently reproduced

The initial targeted selection returned **80 passed, 2 failed**. An isolated `git archive HEAD` desktop snapshot was built and only the two failing methods rerun. The baseline returned **1 passed, 2 failed**, reproducing the same assertion failures:

1. `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`: expected one RBP result notification after direct attachment without selecting an active RBP panel; observed zero.
2. `Dragging_exclusively_owns_position_then_restores_previous_playback_state(true)`: expected paused `IsPlaying` during drag; observed true. The `false` case passed on the baseline.

These failures remain outside this registration refactor. They are not presented as passing or silently repaired by changing the tests.

## Evidence and gaps

Machine-generated evidence remains ignored under `_temp/review-registration-results/`: `review-registration.trx`, `review-registration-final.trx`, `review-registration-focused.trx`, and `baseline.trx`. Baseline sources and build outputs also remain under `_temp/`; no test-output cleanup was performed.

No full backend/desktop test suite, hardware session, real recording replay or manual WPF operation was performed. Existing package compatibility and preview SDK warnings remain. Automated wiring checks do not establish live chart appearance or scientific validity. No commit, push or archive is claimed by this implementation task.
