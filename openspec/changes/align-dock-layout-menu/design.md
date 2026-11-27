# Design

## Context

See proposal.md. Both workspace views already use `DockLayoutPresetMenu.Open`, while their XAML defines the same default-styled `ContextMenu` independently.

## Goals / Non-Goals

**Goals:** Make both menus visually consistent with the shared filter dropdown and explicitly anchor them to the button.

**Non-Goals:** Change menu commands, preset behavior, or other context menus.

## Decisions

- Add reusable context-menu, menu-item, and separator styles to `AnalysisStyles.xaml`, already shared by both workspaces.
- Apply those styles in the two existing menu declarations and keep the common open helper as the sole placement owner.
- Set `PlacementTarget`, `PlacementMode.Bottom`, and zero offsets before opening. WPF may make a viewport adjustment at screen edges.
- Verify the popup's actual screen coordinates and rounded border in a WPF test. The test positions the trigger away from screen edges so it measures the normal anchored placement.

Alternatives considered: duplicating styles in each view would drift; replacing the menu with a ComboBox would change the command-oriented menu interaction and add unnecessary state.

## Risks / Trade-offs

- **Risk:** WPF may shift a popup near the work-area boundary. **Mitigation:** Keep it button-targeted and test the normal placement; work-area adjustment is allowed by the spec.

## Migration Plan

No persisted state or data migration is needed. Roll back by reverting the shared styles, the two XAML style references, the explicit offsets, and the corresponding test.
