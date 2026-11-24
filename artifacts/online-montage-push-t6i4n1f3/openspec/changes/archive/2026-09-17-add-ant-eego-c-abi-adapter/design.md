## Native Boundary

```text
ANT/eego hardware
  -> vendor eego-SDK.dll C ABI (user-installed, x64)
  -> AntEegoNativeApi (runtime loading and ABI checks)
  -> AntEegoAcquisitionAdapter / AntEegoStream
  -> existing AcquisitionCoordinator
  -> raw chunks, display ring buffer, bounded analysis bridge
```

The adapter uses `NativeLibrary.Load` and function-pointer delegates rather
than compile-time DLL references. A missing DLL therefore leaves the desktop
application usable and the default adapter can remain selected. The vendor DLL
is never copied into build output or source control.

## Lifecycle

The vendor documentation permits at most one live stream. A process-wide
native runtime serializes calls and allows one stream owner. Opening validates:

1. x64 process and explicit DLL file;
2. documented SDK version;
3. discovered device identity and requested sampling rate;
4. configured reference/bipolar ranges against device-reported ranges;
5. actual stream channel order and exactly one sample-counter channel.

Stream disposal closes stream then amplifier. Native runtime release happens
only after the stream is closed. No WPF dispatcher participates in this path.

## Data And Time

The C ABI `get_data` payload is sample-major doubles. The adapter obtains
channel count from the opened stream and verifies byte alignment before copying
the payload. It assigns per-channel units: EEG `V`, counter `count`, trigger
`code`; IMU units remain explicitly `vendor_native` pending hardware evidence.

The adapter reads the sample counter from its actual stream position. It
rejects non-finite, non-integral, negative, or non-consecutive counter values
inside one batch. The existing coordinator audits cross-batch gaps. PC receive
UTC is retained only for transport diagnostics.

The SDK does not supply electrode labels, only channel index and type. The
adapter records `Label = null`; a future cap/montage configuration change must
map physical channels to electrode labels explicitly.

## Alternatives

- Reuse `eego-wrapper.dll`: rejected for now. It exists only inside ignored
  external application distributions, its redistribution/runtime contract is
  unverified, and direct C ABI avoids a second opaque managed boundary.
- Default to first reference/bipolar range: rejected. It loses acquisition
  configuration provenance and can silently change scale semantics.
- Treat physical channel index as F3/Fz/Oz: rejected. The SDK does not make
  that claim.

## Verification

- Unit tests cover native device-ID parsing, channel-type/unit mapping, range
  selection, sample-counter validation, and missing SDK behavior without
  requiring hardware.
- Optional local smoke test loads a user-provided x64 DLL and verifies ABI
  version only; it does not count as device validation.
- Hardware acceptance is manual and must cover discovery, open, actual channel
  order/types, counter continuity/reconnect behavior, sustained capture,
  stop/close, gap audit, and raw recording review.
