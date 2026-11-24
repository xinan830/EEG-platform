## Context

The floating electrode map currently allows placement only when `ElectrodeLocation` is unavailable. A resolved standard location therefore cannot be adjusted for a user's diagram needs, even though manual normalized 2D positions are already stored separately from standard XYZ metadata.

## Decisions

- Allow placement for any selected EEG input, whether or not it has a standard location.
- Continue storing the manual position in `ManualMapPosition`; projection already prefers this 2D value when present.
- Preserve `ElectrodeLocation` exactly. Manual placement changes only the illustration and must not rewrite MNE coordinates or claim patient-specific measurement.
- Render manually overridden positions using the existing amber style so the override is distinguishable from unmodified blue standard positions.
- Preserve each row's resolved template location separately from the manual override. The restore-all action clears every EEG row's override and returns standard channels to their template projection; custom channels without a template location become unavailable on the map again.
- Treat the manual 2D position as display metadata: validate its normalized range and persist it, but exclude it from the channel signal-identity fingerprint so moving a point does not invalidate or block existing montage references.
- Represent hardware REF and GND as separate map landmarks, not EEG rows or standard MNE scalp snapshots. Their initial diagram positions are user-confirmed midpoints between Pz/Cz (REF) and Fz/Fpz (GND), projected from the colin27_1020 template; persist normalized 2D positions in the profile, allow dragging either marker independently, and restore both with the map reset action. Keep hardware wiring labels and acquisition metadata unchanged.

## Risks / Trade-offs

- The displayed point may intentionally differ from the standard template. The existing diagram-only label and amber color communicate that distinction; scientific signal data and spatial coordinates remain unchanged.
- REF/GND diagram positions are display metadata only. Treating either landmark as a recorded EEG signal or changing device reference/ground provenance would cross the acquisition contract boundary.
