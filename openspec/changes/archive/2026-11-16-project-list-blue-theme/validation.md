# Validation

- `openspec validate project-list-blue-theme --strict --no-interactive`: passed.
- Desktop Release WPF build: passed with zero warnings and zero errors.
- Relevant project and WPF view tests: 69 passed, zero failed (including the concurrently developed project-directory behavior).
- Static review: project list is the initial shell view; the status filter defaults to “进行中”; primary interface accents use the shared blue palette while semantic status and EEG channel colors remain distinct.
- `git diff --check`: passed.
- No time, unit, channel-order, acquisition-data, backend, HTTP, or WebSocket contracts changed.
- Shared blue resources and existing oversized view responsibilities are registered in `docs/code-size-policy.json`; all 46 changed C#/XAML source/test files pass the size assessment. Existing views over 600 lines have responsibility/split assessments; no palette-only mechanical refactor was performed.
- Both added `desktop/wpf-shell` requirements are synchronized to the main spec. Ordinary validation passes for all 89 main specs; strict validation still reports existing placeholder Purpose and long-description warnings in the repository.
- Release validation suppressed existing NU1701 package compatibility warnings. Real device acquisition and manual visual acceptance were not performed.
