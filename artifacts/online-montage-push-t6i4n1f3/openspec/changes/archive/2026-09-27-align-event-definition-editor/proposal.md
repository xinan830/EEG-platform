## Why

The new event-definition editor exposes fields that were not fully connected to
the event contract. In particular, keyboard capture can misinterpret a lone
Alt key, and a held event shortcut can generate repeated recording events.

## What Changes

- Generate a stable unique event Code when a new definition is created, show it
  read-only, and reject changes to an existing Code at the service boundary.
- Connect the event editor's name, description, color, shortcut, scope,
  enabled state, and save/cancel actions to definition validation and storage.
- Limit descriptions to 200 characters and reject over-limit values at the
  service boundary.
- Treat a Global shortcut as conflicting with the same key in either workspace;
  ignore lone modifier keys and key-repeat events at the UI boundary.
- Preserve existing definition/history data and report validation failures
  without navigating away from the editor.

## Impact

The change affects desktop event-definition editing and keyboard event entry.
Existing Codes are preserved; only newly created definitions receive generated
Codes.
It does not alter EEG samples, sample-coordinate timing, channel order, units,
filtering, or recording-event provenance. No storage migration is needed.

## Non-Goals

- Redesign the user's XAML layout.
- Change existing recording-event time semantics or raw EEG persistence.

## Risks And Rollback

Blocking key-repeat means a held key creates one event per physical key press,
which is intentional for manual annotation. The editor and input behavior can
be reverted independently without modifying persisted records. Event metadata
write failures must never interrupt raw acquisition.
