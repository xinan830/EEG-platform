## Why

The desktop EDF/BDF writer duplicates the backend's recording access and makes independent format verification harder. Move complete raw recording export to Python with bounded reads and one tested EDF+/BDF+ writer.

## What Changes

- Python writes complete raw WPF recordings using pyEDFlib; MNE independently verifies both formats.
- Desktop registers the raw recording, requests an export package, and saves its signal and metadata files locally.
- Remove SharpEDFlib and the C# signal writer. Preserve raw values, channel order, event sample coordinates and provenance; reject gaps and non-finite samples.
- Bound concurrent exports and source reads; temporary packages are cleaned after delivery or failure.

## Capabilities

### New Capabilities
- `recordings/signal-export`: Backend raw EDF+/BDF+ export API and package lifecycle.

### Modified Capabilities
- `desktop/review-signal-export`: Desktop delegates format writing to Python while retaining the save dialog and whole-record range.

## Impact

Affected: backend export service/router/models, dependencies, WPF HTTP client and export workflow, focused backend/desktop tests. No database migration or algorithm changes. Source files are immutable and scientific units remain V internally with explicit uV export. Existing exports stay readable; the new metadata sidecar has an explicit schema. Non-goals: filtered/montage-derived signals, inferred channel labels, automatic sentinel cleanup, discontinuous EDF+D/BDF+D, and other formats. Migration replaces the desktop writer only after both real-data formats pass read-back checks.
