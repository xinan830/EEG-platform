# Proposal: Project list visual refresh

## Why

The project list distributes six columns evenly, gives the selected row a large blue fill, and groups four icon actions at the same visual weight. This makes the only row look disconnected and makes the page feel less coordinated.

## What Changes

- Give project name and description more width than count, status, and actions.
- Reduce selection and status emphasis while preserving clear keyboard focus.
- Align row height, table header, title, and filter spacing for a calmer hierarchy.
- Keep all existing row actions available and preserve their commands.

## Impact

- Affected: WPF project-list presentation only.
- No project data, filtering, paging, navigation, acquisition, or persistence behavior changes.
- No dependency, API, EEG time/unit/channel, or storage changes.
- Rollback: restore the prior project-list XAML styles and column widths.
