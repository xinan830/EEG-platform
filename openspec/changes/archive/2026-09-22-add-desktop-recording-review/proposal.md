## Why

Desktop projects can list locally recorded EEG sessions, but a user cannot open
one of those sessions for waveform review. The immutable raw chunks already
retain the actual stream channel table and acquisition configuration snapshots,
so review should use those recorded facts instead of importing the recording as
an unrelated file or fixing the display to one montage forever.

## What Changes

- Add a project-recording `回溯` action that opens the selected recording in an
  immersive desktop review workspace.
- Read the local manifest, audit trail, and sample-major float64 chunks through
  a bounded, random-window reader; never load or rewrite the complete recording.
- Default the review to the montage snapshot selected at acquisition time.
- Distinguish the immutable acquisition montage from the current viewing
  montage and allow switching to any saved montage whose required source
  channels are available in the recording.
- Keep the current absolute recording position and visible duration when a
  montage switch changes the derived output count, for example from 20 traces
  to 13 traces.
- Keep trigger and sample-counter columns available for event/time semantics,
  but exclude them from EEG montage outputs.

## Impact

This adds a read-only desktop review path and new WPF navigation/state. It does
not change raw samples, acquisition timing, live display behavior, persisted
project ownership, or scientific analysis reference semantics.

## Non-Goals

- Running scientific algorithms or creating reports from the review page.
- Editing channel or montage configurations inside a historical recording.
- Converting local raw recordings to BDF/EDF.
- Treating a viewing montage change as an analysis Run.

