## Why

The channel configuration page currently shows its save action whenever a draft is editable, including when nothing changed. After saving, it closes the draft, which prevents a clear save-again workflow. Operators also need an explicit save-or-discard choice when leaving a modified draft.

## What Changes

- Track whether the editable channel configuration draft differs from its saved or initialized baseline.
- Show the save action only while the draft has unsaved changes, and keep the editor open after saving so later edits can be saved again.
- Confirm when leaving a modified channel configuration; save before leaving on Yes and discard on No.

## Capabilities

### New Capabilities
- `desktop/channel-configuration-draft`: dirty-state and leave-confirmation behavior for channel configuration editing.

### Modified Capabilities
- None.

## Impact

This changes only WPF channel configuration draft state and navigation behavior. It does not change EEG samples, channel order, units, API payloads, or scientific calculations.
