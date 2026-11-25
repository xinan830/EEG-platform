# Proposal

## Why

The backend can already parse imported EDF/BDF files, but the desktop has no project workflow for importing them or opening them in review. Users therefore cannot bring externally recorded EEG into the same project-based review and offline-analysis workflow as platform recordings.

## What Changes

- Add a project-level desktop import flow for EDF and BDF files, associated with an existing or newly created Session in that project.
- Preserve each imported source file unchanged in the backend's managed recording store and register its metadata and immutable identity; link the recording to the selected project's Session by ID without duplicating the signal file.
- Open imported recordings in the existing review workspace using the backend's bounded EDF/BDF read path, retaining the established review presentation and offline algorithm workflow.
- Preserve verified channel and montage provenance from the export sidecar, and make the existing offline algorithm panels execute against the imported recording's backend recording ID without WPF-directory registration.
- Make import cancellable and bounded for large files; reject unsupported, malformed, ambiguous, or incomplete input without publishing a partial recording or Session association.

## Capabilities

### New Capabilities

- `desktop/recording-import`: Import external EDF/BDF recordings into a project and associate them through a Session.

### Modified Capabilities

- `data/recordings`: Define streamed import, metadata validation, immutable source identity, and registration behavior.
- `data/research-projects`: Associate imported recording identities with the chosen project Session without duplicating signal data in project metadata.
- `desktop/recording-review`: Open imported EDF/BDF recordings through the existing review experience while preserving bounded reads and source provenance.

## Impact

- Desktop project recording list, import dialog, Session selection/creation flow, and review-source selection.
- Existing `POST /api/recordings/import` and recording metadata/read services; its current whole-file in-memory upload path must become bounded and cancellable.
- Project Session APIs and persistent source identity; no scientific formulas or algorithm result contracts change.
- Compatibility: existing WPF recordings and current recording APIs remain readable; imported source channel order, raw labels, channel types, units, sampling rate, duration, channel mapping, montage provenance, and annotations remain traceable. Internal signal arrays remain V/float64 and public waveform values remain µV.
- Migration: retain the current recording identity fields and import behavior for existing records; add only a versioned, non-destructive migration if desktop-project identity linkage requires persisted fields. Never rewrite or delete existing source files.
- Non-goals: importing arbitrary formats, modifying acquisition, changing algorithm formulas or result contracts, automatic semantic channel inference, or exporting back to EDF/BDF as part of this change.
