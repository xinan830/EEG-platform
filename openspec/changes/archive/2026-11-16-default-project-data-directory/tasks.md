# Tasks

## 1. Compatibility and storage behavior

- [x] 1.1 Add focused compatibility tests for explicit custom paths, existing-project rename stability and populated-project directory protection; verify the preserved behaviors before changing defaults.
- [x] 1.2 Implement default path resolution and safe directory-name validation in the projects module; verify temporary-root tests cover name-only creation, recordings creation, invalid/reserved names, traversal and existing destinations.
- [x] 1.3 Integrate default resolution into project saving without migrating existing paths; verify directory-write failure does not add an index entry and existing directory data is preserved.

## 2. Project editor

- [x] 2.1 Display the automatically generated location and remove mandatory folder selection while retaining optional custom selection; verify editor tests cover name changes, custom selection, existing-project edits and cancellation without directory creation, and build WPF.
- [x] 2.2 Document executable-relative data storage, development output cleaning, permission errors and preservation on upgrade in the current desktop instructions; assess touched files against size policy and register any required evaluations.

## 3. Integration validation

- [x] 3.1 Run relevant project/editor desktop tests, WPF compilation, git diff --check and openspec validate default-project-data-directory; record outcomes and limitations. Backend tests are not applicable because no backend or scientific contract changes.

## Workflow follow-up

- [x] Finalize the already validated project-list-blue-theme change separately under the repository's apply guidance; preserve all unrelated work.
- [x] Archive this change after implementation and validation, retaining a concise validation report.
