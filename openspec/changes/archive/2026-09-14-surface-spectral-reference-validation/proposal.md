## Why

Independent spectral validation is available through an API but not the local
research workbench, forcing users to construct requests manually.

## What changes

- Add a read-only workbench control that submits the active range and selected
  channel order to the existing independent reference endpoint.
- Render only backend-returned validation status, errors and evidence summary.
- Add an export link that attaches the returned validation ID to an existing
  result export.

## Non-goals

The browser does not calculate PSD, compare vectors, alter tolerances or
interpret clinical meaning.
