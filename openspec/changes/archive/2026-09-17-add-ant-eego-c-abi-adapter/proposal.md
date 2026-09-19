## Why

The desktop acquisition core has a typed, testable lifecycle but has no real
hardware adapter. The locally available ANT/eego material provides the vendor
published C ABI and x64 `eego-SDK.dll`; no stable, distributable .NET wrapper
has been accepted for this product.

## What Changes

- Add an optional x64 ANT/eego adapter that loads a user-provided
  `eego-SDK.dll` through its documented C ABI at runtime.
- Discover actual amplifiers, their native IDs/serials, supported sampling
  rates, channel index/type table, and available reference/bipolar ranges.
- Open only one EEG stream process-wide, retaining the actual returned stream
  channel order and unit for every channel.
- Decode `prefetch`/`get_data` data as sample-major doubles without unit
  conversion. Derive batch counters only from the actual sample-counter
  channel and reject non-integral or non-contiguous counters.
- Require configured reference and bipolar ranges; validate them against the
  opened device rather than silently taking a default range.
- Keep the vendor DLL, drivers, headers, and external examples ignored. The
  product repository contains only its own adapter code and documented ABI
  constants.

## Non-Goals

- No bundled vendor SDK, driver installation, clinical claim, or certification.
- No fabricated electrode names, montage, impedance reading, trigger output,
  Python analysis ingestion, or WPF acquisition UI redesign.
- No claim of real-device acceptance until hardware tests produce evidence.

## Compatibility And Migration

The existing unavailable adapter remains the default. The ANT adapter is
opt-in through an explicit DLL path and configuration. Existing web APIs,
SQLite ownership, raw recording format, Python algorithms, and Vue frontend
remain unchanged. Removing the adapter leaves the acquisition core available
with its unavailable default adapter.

## Scientific Contract Impact

The adapter preserves device-returned sample-major doubles and per-channel
units. EEG values are V, sample counters are count, triggers are code. PC
receive time remains diagnostic metadata only. The actual sample counter and
sampling rate establish relative scientific time; a discontinuity becomes an
audited gap or stream fault, never synthetic data.
