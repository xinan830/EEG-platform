# Architecture debt cleanup validation report

Date: 2026-11-01

## Completed slices

- Classified the suspected TNK `legacy_runtime` path as current Crimson/Zenlite
  science. It remains in place. Historical recording/montage readers remain in
  place.
- Moved `DesktopWorkspaceViewModel` from the retired
  `BrainPlatform.Desktop.ViewModels` namespace to
  `BrainPlatform.Desktop.Application` and updated all consumers.
- Added `IDesktopNavigation`; feature Views no longer cast their host window to
  `MainWindow` for navigation.
- Added `MainWindowViewSet`; `App` now owns persistent feature-view
  construction and passes the set into `MainWindow`.
- Added `**/.isolated-build/` to `.gitignore`.

## Automated evidence

- Focused backend boundary run: 17 passed; one pre-existing TNK evidence assertion
  failed because the current result now includes four preprocessing-context fields
  that the test still compares as an exact old dictionary. This cleanup change does
  not touch backend scientific code, so the failure remains recorded rather than
  changing the current evidence contract.
- Desktop application build to an isolated output: passed, 0 errors.
- Desktop test project build to an isolated output: passed, 0 errors.
- Desktop tests: 1123 passed, 4 baseline failures (analysis notification, review
  drag/playback state, montage-reference reconstruction, and waveform refresh).
- OpenSpec strict validation: 83/83 passed.
- `git diff --check`: passed.
- Static search: no old workspace namespace and no direct View-to-`MainWindow`
  navigation casts remain.

## Manual acceptance evidence

- The running WPF client loaded the changed shell and resources without a BAML or
  resource-load error.
- Project list -> project detail -> project list navigation worked and preserved
  the existing project/recording list.
- The data-review navigation displayed its explicit not-yet-connected state and
  did not create or modify data.
- The new-project dialog opened, displayed its fields and owner, and closed via
  the title-bar close action without creating a project.

## Known limits

- The full desktop test assembly currently has four unrelated baseline failures
  in waveform refresh, review navigation race, analysis notification, and
  montage-reference reconstruction tests. They are retained as failures and
  were not changed by this structural slice.
- Manual WPF navigation/dialog/BAML acceptance has not been performed in this
  session.
- Existing `.isolated-build` and diagnostic artifact directories were verified as
  generated output and ignored, then deleted through the user-authorized cleanup
  command. Normal `bin/obj` outputs remain because the running desktop process
  uses them. No recordings, logs, databases, or user data were touched.

## Semantic boundary check

The implementation diff contains only WPF composition/navigation ownership and
repository ignore/documentation changes. No backend scientific implementation,
filter, algorithm version, time/unit/channel mapping, public API, historical
recording reader, or persisted-result reader was changed in this slice.

This is engineering validation only; it is not clinical validation.
