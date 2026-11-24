## Context

The review view model exposes the recording's first and last sample counters, manifest, event list, and an `IRecordingAccessSession` that reads original samples by half-open sample range. Reads preserve discontinuities as separate segments. The desktop target is Windows x64, and no EDF/BDF writer is currently referenced.

## Goals / Non-Goals

**Goals:** Write EDF+ or BDF+ directly from bounded local source reads; keep the UI responsive; ensure failed/cancelled writes do not leave a file that looks complete.

**Non-Goals:** Change analysis, upload recordings to FastAPI, alter source storage, create derived filters/montages, or encode acquisition lifecycle gaps as continuous values.

## Decisions

- Use SharpEDFlib 1.26.0, a BSD-3-Clause .NET binding for EDFlib with EDF+/BDF+ write support and Windows x64 native runtime. This keeps recording data local and avoids a custom binary-format implementation or sending raw signals through HTTP. Its native runtime must be exercised in the Windows build/test environment.
- Implement the writer in the Review module and stream bounded source reads. Keep the WPF file picker in the view, while sample range resolution, source validation, event selection, and export orchestration stay in the view model/service boundary.
- Export the complete half-open raw sample-counter range `[first_sample_counter, last_sample_counter + 1)`. Preserve the source sample rate and the `Reference`/`Bipolar` channel order. Convert only `V` source units explicitly to microvolts; reject unknown units. Divide long recordings into complete data records no longer than 60 seconds, without dropping or padding samples.
- EDF/BDF record data cannot safely represent a missing source range as ordinary continuous samples. Reject any interval whose returned segments do not cover the full requested range contiguously; never fill a gap.
- Write to a unique temporary file beside the target and move it into place only after the native writer closes successfully. Delete the temporary file on failure or cancellation.
- Include only resolved review events from this recording whose recording-relative sample onset is in the half-open exported range. Store their onset relative to the first exported sample and duration from sample counts.
- For channel labels that cannot be represented in EDF/BDF's 16-character printable ASCII field, write a stable channel alias and an adjacent `.labels.json` mapping file; preserve normal labels directly in the signal header.
- Include the recording/channel mapping and available hardware configuration in that same sidecar, excluding serial and device-instance identifiers, so an alias remains traceable without exporting those identifiers.
- Express EDF data-record duration in exact 10-microsecond units to avoid floating-point truncation in the header.

Alternatives considered: MNE/pyedflib would require routing local desktop samples through the Python backend or introducing a second local runtime; a hand-written EDF/BDF encoder duplicates a binary standard and is harder to validate. Gap-aware discontinuous timelines are deferred.

## Risks / Trade-offs

- [Native library packaging or licensing metadata is incomplete] → Verify package license and Windows x64 runtime loading through an actual export/read-back test; do not hand-ship native binaries separately.
- [EDF/BDF quantization changes stored float64 values] → Validate sample count, labels, sample rate, and bounded round-trip error; the exported file is a derived format and does not replace source data.
- [The recording contains a pause or dropped samples] → Fail with an explicit message and leave the target untouched; discontinuous timelines are deferred.

## Migration Plan

No data migration. Add the dependency, deploy with the WPF application, and validate both formats by reopening generated files. Rollback is removal of the export action and dependency; existing recordings and exports are independent files.
