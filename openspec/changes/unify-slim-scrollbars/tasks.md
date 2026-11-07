## Implementation

- [x] 1. Trace application resources, explicit consumers and scrolling semantics; record compatibility and scope.
- [x] 2. Reuse the shared style globally and correct both orientations, hover and drag presentation.
- [x] 3. Verify WPF scroll containers and Review panel tests, OpenSpec strict validation, code size and whitespace; record evidence.

Validation: 9 WPF tests passed, 0 failed, 0 skipped, covering panels, DataGrid, ListBox and TextBox in both orientations, actual 6 DIP tracks/4 DIP thumbs, paging commands, drag events, existing keyed style and Review panel regressions. Report: `_temp/slim-scrollbar-results/slim-scrollbars.trx`. OpenSpec strict and `git diff --check` passed. Shared XAML: 82 lines; new test: 144 lines. No backend changes or tests; physical-pointer and real-recording visual acceptance remain for the user.
