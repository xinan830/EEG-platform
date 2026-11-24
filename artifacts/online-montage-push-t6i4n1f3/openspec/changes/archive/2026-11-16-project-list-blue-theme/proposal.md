# Proposal

## Why

The desktop currently opens on an overview page, so users who manage projects must navigate away from the landing page before beginning their work. The interface also uses green accents that do not match the requested calm, consistent blue identity.

## What Changes

- Open the desktop application directly on the project list.
- Make “进行中” the initial project status filter while keeping other filters available.
- Establish a restrained blue palette for shell navigation, primary actions, and selection states; use white and gray as neutrals and remove green as a primary interface accent.
- Keep status semantics and distinct EEG channel colors legible; define these as semantic/data colors rather than expanding the core brand palette.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/wpf-shell`: Define the initial project-list route and shared blue visual identity for the desktop shell.

## Impact

- Affected areas: WPF startup navigation, project-list filter defaults, shared resource dictionaries, and project/shell views that currently use green or inconsistent primary accents.
- Compatibility: Existing project records, status values, filters, and navigation actions remain available; only the initial view and initial filter change.
- Non-goals: Changing project status data, EEG calculations or channel ordering, backend APIs, or introducing a new UI dependency.
- Scientific-contract impact: None. Time, units, channels, acquisition, and analysis behavior are unchanged.
- Migration strategy: No data migration is needed. The previous initial overview remains reachable through existing navigation if it is still exposed as a page.
