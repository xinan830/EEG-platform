# ANT/eego C ABI Adapter Validation Report

Date: 2026-09-17

## Scope

`add-ant-eego-c-abi-adapter` adds an opt-in .NET 10 x64 ANT/eego acquisition
adapter. It dynamically loads a user-provided `eego-SDK.dll`; vendor binaries,
drivers, headers, and example implementations remain outside the repository.

## Verified

- The adapter requires an explicit SDK path and explicit reference/bipolar
  ranges. It validates configured ranges against the selected device before
  opening an EEG stream.
- Discovery, sampling-rate lists, channel type/index lists, ranges, stream
  opening, buffered read, and error mapping use the documented vendor C ABI.
- The process permits one live vendor stream. Stream disposal closes stream,
  then amplifier, then releases the vendor runtime.
- Incoming data is retained as sample-major `float64`; the opened channel table
  carries per-channel units. The adapter does not invent electrode labels.
- The actual sample-counter channel is validated for finite, non-negative,
  integral, within-batch consecutive values. The existing coordinator audits
  cross-batch discontinuities as gaps.
- Raw manifests now retain hardware configuration, including selected ranges.

## Automated Evidence

```text
dotnet build desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: succeeded, 0 warnings, 0 errors

dotnet test desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: 20 passed, 0 failed

openspec validate add-ant-eego-c-abi-adapter --strict --no-interactive
Result: valid

git diff --check -- desktop-client openspec/changes/add-ant-eego-c-abi-adapter .gitignore
Result: no whitespace errors
```

The tests cover native channel mapping and units, no guessed labels, valid and
invalid sample counters, invalid device identities, missing explicit ranges,
missing SDK handling, raw-manifest hardware configuration, and the existing
acquisition coordinator lifecycle.

## Not Yet Verified

No ANT amplifier was connected during this change. Therefore this report does
not establish actual DLL ABI compatibility, device discovery, counter reset or
overflow behavior, reconnect behavior, sustained capture, USB fault handling,
or data quality. Those are hardware acceptance activities documented in
`desktop-client/README.md` and must be completed before making collection or
scientific-use claims.
