# Amplifier Setup Workflow Validation Report

Date: 2026-09-17

## Verified Behavior

- The primary page no longer asks an operator to type an SDK path or either
  amplifier range.
- SDK selection is a file-picker action inside the collapsed amplifier-settings
  surface.
- A connection test can run with only an SDK file. Discovery returns devices,
  sampling rates, reference ranges, and bipolar ranges from the amplifier.
- Stream opening still validates user-selected ranges against the current
  device. No first/default/guessed range is selected.
- The recording directory and raw channel table are in the advanced section.

## Automated Evidence

```text
dotnet build desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: succeeded, 0 warnings, 0 errors

dotnet test desktop-client\BrainPlatform.Desktop.slnx -c Release --no-restore
Result: 24 passed, 0 failed

openspec validate simplify-amplifier-setup --strict --no-interactive
Result: valid
```

No physical amplifier was connected; actual device connection remains subject
to the documented hardware acceptance procedure.
