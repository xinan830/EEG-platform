## Why

Desktop acquisition currently accepts a free recording directory, so a valid
raw session can exist without a research-project identity. That breaks the
project-first workflow and leaves provenance unable to answer which project
owned a recording.

## What Changes

- Add persistent desktop research projects with user-selected project folders.
- Replace static project examples with project CRUD and project-scoped recording discovery.
- Require a selected project before acquisition can start.
- Remove acquisition from global navigation and route project acquisition
  through a preparation page for montage and sampling-rate selection.
- Write raw sessions below `<project>/recordings/` and persist an immutable
  project identity and snapshot as a top-level manifest field.
- Removing a project from the local index does not delete its folder or raw data.

## Impact

This changes desktop project management and the acquisition request contract.
It does not change raw EEG samples, device time semantics, display montage
math, or backend scientific analysis.
