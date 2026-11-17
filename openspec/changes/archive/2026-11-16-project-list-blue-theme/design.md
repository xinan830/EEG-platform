# Design

## Context

See proposal.md for motivation and specs/desktop/wpf-shell/spec.md for observable behavior. The WPF shell currently assigns the overview view in `MainWindow` construction, while the project workspace filter initializes to “全部”. Brand and status colors are distributed among shared and page-level XAML resources.

## Goals / Non-Goals

**Goals:**

- Make the existing project list the initial landing view and default its status filter to “进行中”.
- Establish up to three shared blue brand accents and apply them to shell navigation and primary project actions.
- Preserve neutral surfaces, semantic status colors, and distinguishable multi-channel EEG traces.

**Non-Goals:**

- Changing project records/status vocabulary, routing APIs, EEG data, channel order, acquisition, or analysis.
- Forcing warning/error/success indicators or EEG trace series into brand colors.
- Redesigning the structure of individual pages beyond the scope needed to apply the shared theme.

## Decisions

- Reuse the existing `ProjectListView` and `ProjectWorkspaceViewModel`; initialize the filter to the existing “进行中” option and set the shell's initial content/navigation through the same path used by project navigation. This avoids introducing a second landing-page implementation.
- Treat “three colors” as three brand accents, alongside unrestricted white/gray neutrals. Status and trace colors remain semantic data colors so red/amber warnings and channel identity remain legible.
- Define the shared brand colors in the existing application resource dictionaries, then update shell/project views to consume those resources rather than maintaining scattered near-duplicate blue literals. Keep the selected blue values documented in one place.
- Preserve the overview's availability according to existing navigation. The startup change alters only the initial view.

## Risks / Trade-offs

- [Risk] Some page-level colors may be semantic while appearing green → classify by meaning before replacement; only remove green where it serves as a brand or action accent.
- [Risk] WPF resource lookup or startup ordering could leave initial bindings/navigation stale → initialize through the existing navigation path and verify the compiled desktop app opens on the list with “进行中” selected.
- [Risk] A strict three-color interpretation could reduce EEG trace distinguishability → keep channel series colors in the data palette, separate from the brand palette.

## Migration Plan

No persistent data migration is needed. Rollback consists of restoring the prior initial overview assignment, “全部” filter default, and prior brand resource values. Verify project filtering and shell navigation after implementation.
