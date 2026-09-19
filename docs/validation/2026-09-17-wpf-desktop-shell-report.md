# WPF Desktop Shell And Acquisition Core Validation

Date: 2026-09-17

## Scope

This change adds the x64 .NET 10 WPF desktop shell and its hardware-neutral
acquisition core. It does not load a vendor SDK or claim that ANT/eego hardware
has been validated.

## Implemented Boundaries

- Backend health is a bounded read-only request to the existing local
  `GET /api/health` endpoint.
- Device, acquisition, and scientific-engine states remain distinct.
- The default adapter returns no devices and cannot open a stream; it does not
  manufacture channels, sampling rates, sample values, or impedance values.
- The coordinator serializes acquisition lifecycle work. It retains the
  device-reported channel table, sample-counter channel, sampling rate, and
  recording start time for a future real adapter.
- Received batches are sample-major float64 values with an explicit unit per
  channel: EEG is V, counter is count, and trigger is code. Counter gaps are
  persisted explicitly; display eviction cannot alter raw persistence.
- Raw data is written before display buffering or optional analysis dispatch.
  The local writer creates a manifest, bounded raw chunks, and JSONL audit
  events. PC receive time remains diagnostic metadata, not sample time.
- The bounded analysis bridge cannot block raw capture and has no authority to
  write the Python backend's SQLite Runs or scientific artifacts.

## Verification

- `dotnet build desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`
  completed with 0 warnings and 0 errors.
- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore`
  passed 10 tests.
- Tests cover backend health parsing, unavailable adapter behavior, sample
  counter gaps and out-of-order failures, bounded display history, raw chunk
  rotation and audit persistence, normal lifecycle completion, and rejected
  start without an installed adapter.
- `openspec validate add-wpf-desktop-shell --strict --no-interactive` passed.
- `git diff --check` reported no whitespace errors for the change files.

## Remaining Hardware Acceptance

The next change must implement and test the vendor C ABI adapter with actual
hardware. Required evidence includes DLL loading, discovery, actual channel
index/type list, supported sampling rates, sample-counter semantics across
chunks and reconnects, sustained capture, gap accounting, stop/close behavior,
and raw recording integrity. This is engineering validation only, not clinical
or certification validation.
