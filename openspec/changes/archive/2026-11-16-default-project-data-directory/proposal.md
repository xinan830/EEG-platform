# Proposal

## Why

Creating a desktop project currently requires a folder selection even though the workstation can choose a predictable location. The user wants project data on the same disk as the application, rather than the Windows documents directory on C:.

## What Changes

- Default new project storage to `<application directory>/data/<project name>` and create its `recordings` child when saving.
- Require only the project name; display the automatically chosen directory without requiring folder selection. Retain optional custom directory selection.
- Reject invalid Windows folder names and occupied destinations with readable errors; never merge a new project into existing data.
- Preserve existing project paths, editing restrictions and data-preserving removal.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/project-acquisition`: Automatic default project directories and creation failure semantics.

## Impact

Touches project editor XAML/view model, project workspace save logic, focused desktop tests and size-policy registration if needed. No new dependencies or backend/API/WebSocket changes. Time bases, units, channel order, signal processing, quality and recording provenance stay unchanged.

No existing data migration is performed. Existing project index entries retain absolute paths. Rollback restores folder selection for new projects without deleting generated directories. Global storage settings, moving old data and installer changes are out of scope.
