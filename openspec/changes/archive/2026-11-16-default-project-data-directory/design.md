# Design

## Context

See proposal.md for motivation. The editor currently requires DirectoryPath; ProjectWorkspaceViewModel.SaveAsync creates the directory and recordings child before persisting ResearchProjectStore. Existing projects store absolute paths. The previous UI theme change remains separate and must not be overwritten.

## Goals / Non-Goals

Goals: name-only default creation with predictable storage, explicit failures, and compatibility with existing paths. Non-goals: global directory settings, moving directories when renaming a project, changing the project-index location, backend changes, or installer customization.

## Decisions

- Resolve the application directory from AppContext.BaseDirectory, never the shell working directory. Use its data child as the default root. Documents/AppData were considered; the user explicitly chose storage beside the application to avoid consuming C: space. During development, this means the build output directory, not the repository root.
- Keep directory resolution and name validation in the projects module, shared by editor preview and save validation. Blank directory input on creation selects the default; existing explicit directory callers remain compatible. Existing-project edits keep their current path when the name changes.
- Show the resolved path without a mandatory marker or mandatory browse step; retain optional browse selection. Create folders only when saving, not on typing or cancellation.
- Trim surrounding whitespace from the name, then reject separators, invalid Windows characters, dot-only/reserved device names and trailing dots when generating a default path. Require a child of the default root. Reject an existing default file or directory, including data retained after project removal; stage the recordings folder under the data root and atomically move it into the final name so a concurrent collision cannot merge data.
- Preserve current custom-directory rules and existing populated-project directory protection. The desktop remains the storage owner. Do not change raw recording contents, time, units, channel order, quality, provenance or HTTP/WebSocket contracts.
- Propagate permission, disk-full and index-save failures through existing readable desktop error reporting; do not silently fall back to C:. Do not add an index entry for directory-creation failure or delete pre-existing data on failure. An empty directory left after index-save failure may require manual removal; no generic storage transaction is introduced.

## Risks / Trade-offs

- [Risk] Installations under protected locations cannot write data → show the failed path and allow an explicit custom directory; never request elevation automatically.
- [Risk] Upgrades, uninstall or moving the application may separate existing absolute paths from the executable → retain existing project paths; users must preserve data when replacing application files. Installer migration is outside this change.
- [Risk] Development output folders can be cleaned → document that data resolves beside the running executable, and keep real recordings outside disposable build output through the optional custom location.
- [Risk] File size → assess touched files against docs/code-size-policy.json; ProjectWorkspaceViewModel already exceeds 400 lines, so keep path validation in a focused projects owner rather than growing that view model with platform rules.

## Migration Plan

No migration or index schema change. Existing directories remain authoritative. Rollback reverts default-path generation and editor behavior without removing created directories. Archive the prior validated theme change separately before applying this one, following apply guidance.

## Testing Strategy

Use temporary data roots to cover name-only creation, exact default root, recordings child, invalid names, occupied destinations and write failure without index mutation. Cover optional custom paths, existing-project rename stability and populated-directory protection. Test editor path preview and cancellation without directory creation. Build WPF and run focused project tests using an isolated output directory if a live executable is locked. Validate OpenSpec and diff whitespace; backend tests are unnecessary because no backend or scientific contracts change.
