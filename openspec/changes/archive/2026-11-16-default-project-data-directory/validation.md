# Validation — 2026-11-16

- Relevant project and WPF view tests: **69 passed, 0 failed, 0 skipped**. Built with `dotnet test` against `BrainPlatform.Desktop.Tests.csproj` using `.artifacts/project-directory-validation` to avoid the running desktop executable's file lock.
- Coverage includes name-only creation and recordings directory, executable-relative default root, Windows reserved/invalid names and traversal, occupied files/directories, concurrent same-name creation, custom location preservation, stable paths on rename, populated-project path protection, write failure without index mutation, data-preserving removal, compiled editor bindings, preview and cancellation without directory creation.
- Release WPF build: passed. Existing NU1701 dependency compatibility warnings were suppressed for this validation; no other build warnings or errors. Tests retain existing unrelated xUnit2013 and CA2022 analyzer warnings.
- `openspec validate default-project-data-directory --strict --no-interactive`: passed.
- Main specs synchronized to `desktop/project-acquisition`; ordinary validation of all 89 main specs passed. Full strict main-spec validation still flags existing placeholder Purpose and long requirement descriptions (including pre-existing descriptions in the touched capabilities); it is not a clean repository-wide strict baseline.
- `git diff --check`: passed.
- All 46 changed C#/XAML source and test files (including prior theme edits) satisfy `assess_file` after documenting existing oversized view responsibilities. The project workspace is 493 lines under its reviewed 500-line ceiling. No mechanical source splitting was performed.
- Repository-wide backend file-size check reports an unrelated existing `backend/app/algorithms/psd/runner.py` over its 420-line allowance (464 lines). No backend source was changed.

## Scope and limits

No backend/API/WebSocket, EEG time, unit, channel-order, quality or provenance contract changed; backend and real amplifier acquisition tests were not run. All project data tests use temporary roots.

No user process was stopped or real project data moved. Existing project index location is unchanged. During development, the default data root is beside the running executable inside the build output; preserve it before cleaning/replacing that output. Index-save failure after successful directory creation may leave an empty directory requiring manual removal; no storage transaction or migration was added.
