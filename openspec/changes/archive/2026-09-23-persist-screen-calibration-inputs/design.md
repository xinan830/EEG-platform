## Context

The WPF screen-calibration view already stores profiles as JSON keyed by the
Win32 monitor name, but the view model loads a placeholder identity before its
window handle exists. The user-visible fields can therefore be empty during
startup, and a non-atomic write can lose the file during an interrupted exit.

## Goals / Non-Goals

**Goals:**

- Keep valid width/height values across application restarts.
- Preserve exact per-monitor profiles while adding a safe fallback for a
  temporarily unavailable or changed monitor identity.
- Keep centimeters as the persisted input unit and derive mm/DIP only at use.

**Non-Goals:**

- No change to EEG units, sampling time, channel mapping, filtering, or backend
  APIs.
- No automatic guessing of physical monitor dimensions.

## Decisions

- Keep the existing JSON file and profile schema for compatibility. Add a
  deterministic latest-valid fallback only when an exact display-key lookup is
  unavailable; exact matches always win.
- Persist only after both values pass the existing physical range validation.
  Text-change previews remain transient until the pair is valid, so a partial
  edit cannot erase a working calibration.
- Write through a temporary file in the same directory and replace the target
  file. This avoids corrupting the entire preference file if the process exits
  during serialization. A registry setting was rejected because it would make
  the profile harder to inspect and migrate.

## Risks / Trade-offs

- [Risk] A monitor replacement may reuse a generic fallback profile with a
  different physical size. → Exact display-key matches are preferred, the
  fallback is used only when identity is unavailable, and the page continues
  to label the values as operator calibration rather than measured hardware.
- [Risk] Auto-persisting valid edits can save a value before the operator has
  finished typing. → Persist only when both fields parse and pass the same
  range checks as the explicit Save command.

## Migration Plan

Existing `screen-calibration.json` files remain readable. The first successful
save rewrites them atomically with the same profile fields. If a new write
fails, the previous file remains intact and the UI reports the failure.
