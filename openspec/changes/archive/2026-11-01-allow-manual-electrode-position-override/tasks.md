## 1. Manual diagram override

- [x] 1.1 Allow dragging any selected EEG channel, including channels with standard-template coordinates.
- [x] 1.2 Verify manual positions override only the diagram projection and leave standard XYZ metadata unchanged.
- [x] 1.3 Add regression tests for manual override projection and selected-standard-channel placement eligibility.
- [x] 1.4 Run focused desktop tests, desktop build, OpenSpec strict validation, and `git diff --check`.
- [x] 1.5 Add a restore-all button that returns standard channels to their template positions.
- [x] 1.6 Cover restore-all behavior for standard and custom channels.
- [x] 1.7 Ensure diagram-only positions do not block standard-location validation or change signal-identity fingerprints.
- [x] 1.8 Run focused desktop tests and build, OpenSpec strict validation, and `git diff --check`.
- [x] 1.9 Add separate persisted REF/GND map landmarks at the user-confirmed default sites and restore them with the default-position action.
- [x] 1.10 Verify hardware landmarks remain outside EEG channel projection and signal identity.
- [x] 1.11 Set REF/GND defaults to the requested template site midpoints and allow each marker to be moved independently.
