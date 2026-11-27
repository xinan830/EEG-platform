# Tasks

## 1. Startup consistency
- [x] 1.1 Add deterministic regressions for settings-wait edits, earlier delayed filter updates and failed-start retry; verify failing cases before the fix.
- [x] 1.2 Guard filter setters throughout startup, drain pending updates and recheck other inputs before opening; verify focused Release tests.

## 2. Closeout
- [x] 2.1 Correct affected size-policy descriptions and limits; verify check_file_sizes.py and git diff --check.
- [x] 2.2 Run related desktop tests, backend gate and strict OpenSpec validation; document exact results and remaining baseline failures.
