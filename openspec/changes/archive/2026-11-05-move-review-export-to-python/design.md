## Context

See proposal.md. The backend already registers raw WPF recordings and provides sample-addressed readers. Desktop analysis registration defaults to acquisition montage, so export must explicitly register `raw`, never reuse the derived analysis identity. pyEDFlib 0.1.42 supports Windows wheels through Python 3.12; Python 3.13 would require MSVC. edfio was evaluated but its writer allocates the whole serialized recording.

## Goals / Non-Goals

Use one backend writer, preserve full samples and metadata, bound reads and concurrency. No change to acquisition, display processing, scientific algorithms or source recordings. Unconfirmed finite sentinel values remain raw and are reported, not guessed or cleaned.

## Decisions

- POST `/api/recordings/{id}/signal-export` accepts a typed EDF/BDF format and returns a ZIP containing `recording.<format>` and its `.labels.json`. The backend owns temporary output, deletes it after download/failure, and never accepts a user destination path. WPF streams the archive to disk then saves the two known members next to the user target.
- A nonblocking single-export gate bounds concurrent memory/I/O. Blocking file work runs in the request worker, separate from the scientific coordinator. Disconnect cancellation is checked between source blocks and data records. The desktop links export to page lifetime and exposes cancellation/status.
- Use WpfSampleReader with a dedicated reader and validated raw registration. Two bounded passes find per-channel ranges then write complete data records. Resolve an exactly representable record duration that divides the total sample count; refuse padding, cropping, gaps or non-finite data. Preserve all Reference/Bipolar channels including unnamed inputs.
- Round physical minima downward and maxima upward to whole uV header bounds; never shrink range. pyEDFlib's decimal formatter can truncate the binary representation of decimal bounds, so integer endpoints avoid inconsistent conversion and serialized calibration. Read the finished header back and reject any mismatched calibration before delivering it. Preserve labels or generate collision-free aliases. Record source identity, original indices/types/units, safe hardware/reference/montage snapshots, raw/unfiltered processing, quantization steps and finite-value quality observations in versioned sidecar. Events use recorded sample coordinates and UTC origin; do not infer triggers from absent event records.
- Use the pinned library's low-level start-subsecond setter with 100-ns ticks; its datetime setter incorrectly multiplies microseconds by 100 instead of 10. MNE currently discards measurement-date subseconds, so verify the encoded origin separately with EDFlib ticks and retain the exact UTC origin in the sidecar. EDF annotations round to 0.1 ms; preserve exact integer sample coordinates in the sidecar.
- The pinned record-duration binding truncates seconds multiplied by 100000; advance the floating argument by one representable step before that conversion to prevent underflow at durations such as 0.009 seconds. Verify actual encoded sample count, rational sample rate and channel count before delivery. Decimal durations such as 32.77 can yield a one-ULP floating rate difference in readers despite a correct header; compare the header ratio as rational numbers rather than requiring binary floating equality.
- Validate with MNE (different reader) and pyEDFlib header inspection, exact sample count/rate, source hashes and per-channel quantization error, including late events and awkward sample totals. Remove the C# writer only with replacement coverage.

## Risks / Trade-offs

- Finite saturated/suspected sentinel data still limits EDF precision -> expose per-channel range/quantization and warnings; BDF offers higher resolution but does not repair source data.
- Multiple files cannot be replaced in a single filesystem operation -> stage both and restore existing destination files if commit fails; failed/cancelled operations must not appear successful.
- Native Windows dependency -> verify compatible runtime installation and packaging. No custom format encoder or second permanent export-only runtime.
- No backend-independent export after migration -> show actionable backend errors; existing exports remain independent.

## Migration Plan

Verify library/runtime in isolation, implement and test backend, route WPF through HTTP, remove SharpEDFlib, run real-recording acceptance and relevant suites. No data migration. Roll back source change to return to the previous writer; never rewrite recordings.

The single backend environment is Python 3.12, pinned by `.python-version` and the supported Python range in `pyproject.toml`. The previous local 3.13 environment is retained only as an inactive rollback backup under `_temp/`; there is no separate permanent export runtime. Use `uv sync --project backend --frozen --extra dev` to reproduce dependencies. Packaging explicitly collects pyEDFlib and checks the runtime before building.
