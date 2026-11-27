# Proposal

## Why

The acquisition and recording-review layout menus still use WPF's default gray context-menu presentation, unlike the white rounded electrode dropdown. Their default sizing and placement make the menu feel detached from the toolbar button.

## What Changes

- Give both layout preset menus the shared white, rounded dropdown style.
- Open each menu directly below its trigger with explicit zero offsets.
- Add a WPF test for the shared style and button-relative placement.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `eeg-workspace-presentation`: layout preset menus share the dropdown style and remain anchored to their trigger.

## Impact

Only WPF presentation and its tests change. Layout commands, compatibility, recording data, scientific settings, APIs, and dependencies remain unchanged. Rollback consists of reverting the shared styles, the two XAML references, and the placement test.
