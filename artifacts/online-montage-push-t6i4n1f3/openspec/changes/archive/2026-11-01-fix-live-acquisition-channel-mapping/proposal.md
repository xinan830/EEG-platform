# Preserve selected mappings during live acquisition

## Why

Acquisition setup reapplies the selected montage's channel snapshot correctly,
but every device-session status notification reloads the device defaults or
legacy saved labels. Live montage projection then omits a custom label such as
F88 or reads an old native input for a reused label such as F3. The opened
stream metadata can remain correct while the live chart is wrong.

## Scope

- Distinguish unchanged physical device facts from acquisition status changes.
- Preserve selected channel mappings and setup choices through stream setup,
  preview, recording, pause, resume and stop.
- Verify both output names and native signal values with synthetic inputs.

## Non-goals

No chart styling, filter mathematics, SDK sample layout, raw-file rewrite,
review compatibility, algorithm changes or unrelated workspace refactor.

## Scientific-contract and compatibility impact

Live derived outputs must use the native inputs declared in the selected
channel snapshot. EEG remains sample-major float64/V and native stream order
does not change. Real device replacement, capability changes and disconnection
must still refresh device-backed setup. No schema migration is required.

## Migration and rollback

Restart the desktop to load the fix, then reopen live preview with the selected
montage. No saved presets or historical recordings are migrated. Reverting the
fix restores the defective status-driven setup reload; it must not be used to
repair or relabel historical raw files.
