# Desktop Minimal Acquisition Workflow Validation Report

Date: 2026-09-17

## Scope

`wire-desktop-acquisition-workflow` wires the existing optional ANT/eego
adapter into a minimal WPF operator workflow. It is intentionally not final UI
and does not add live scientific analysis, waveform rendering, impedance, or
trigger controls.

## Verified

- The desktop starts with no applied hardware configuration and no fabricated
  device, channel, rate, waveform, or result.
- A user can locally save and explicitly apply a DLL path, reference range,
  bipolar range, and raw-recording directory.
- Changing any connection field invalidates the applied configuration, so scan
  and recording cannot silently use the prior settings.
- Scan displays only SDK-returned devices; rate selection is populated only
  from the selected device's returned sampling-rate list.
- Start delegates to the existing coordinator. Returned channel position,
  native index, type, label availability, and unit are displayed only after
  a stream opens.
- Application shutdown disposes the acquisition runtime, causing its
  coordinator to stop/close any active local recording and release its stream.
- SDK and lifecycle errors are displayed as operational messages; no fake
  device or data is substituted.

## Automated Evidence

```text
dotnet build desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: succeeded, 0 warnings, 0 errors

dotnet test desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: 23 passed, 0 failed

openspec validate wire-desktop-acquisition-workflow --strict --no-interactive
Result: valid
```

Tests cover local settings round-trip, configuration being required before
discovery, missing SDK error behavior, and all earlier raw persistence,
continuity, adapter-contract, and coordinator tests.

## Limit

No physical ANT amplifier was connected. This validates the desktop workflow
and its non-fabrication behavior, not the vendor driver, actual stream ABI,
or scientific validity of collected data. The hardware acceptance checklist in
`desktop-client/README.md` remains required before research use.
