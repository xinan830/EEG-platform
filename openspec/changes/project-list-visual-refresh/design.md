# Design

## Decisions

- Use weighted columns shared identically between the table header and rows: name 1.25, description 2.1, count 0.65, created time 1.15, status 0.8, actions 1.35.
- Bind the table width to the scroll viewport and stretch the description within its cell; show a single line with ellipsis and preserve the full description in a tooltip.
- Use a blue left-edge selection indicator on a white row instead of filling the entire row, so selection is clear without turning the table into a blue block.
- Show the stable project number beneath the project name to give the primary column a useful second line when descriptions are empty.
- Group row actions in a quiet neutral container while retaining all four existing actions and handlers.
- Keep completed status neutral and paused status amber so status meaning does not compete with the brand palette.
- Increase row/action hit targets modestly and restore the framework focus indicator for keyboard navigation.
- Preserve all four row actions and their existing click handlers; the group makes them read as one control area without hiding them in a menu.

## Risks

- Narrow windows may still need horizontal scrolling; the existing table already supports it.
- The XAML uses the same weighted column definitions for the header and row to prevent alignment drift.

## Data Contract

No change to data, filters, paging, project selection semantics, navigation, acquisition, persistence, backend, or scientific contracts.
