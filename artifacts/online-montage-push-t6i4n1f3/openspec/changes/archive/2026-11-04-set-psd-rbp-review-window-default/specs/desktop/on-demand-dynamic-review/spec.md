## ADDED Requirements

### Requirement: PSD and RBP Review use a ten-second default analysis window
The data Review PSD and RBP panels SHALL default to a 10-second dynamic analysis window and a 1-second refresh step, as declared by each algorithm's official catalog policy. This outer analysis window SHALL remain distinct from the four-second Welch segment with 50% overlap; this requirement does not alter Welch computation parameters.

#### Scenario: Open the PSD Review panel with catalog defaults
- **WHEN** the user opens the data Review PSD panel without changing its dynamic policy values
- **THEN** the desktop request uses a 10-second dynamic analysis window and a 1-second refresh step
- **AND** the PSD calculation retains its declared four-second Welch segment and 50% overlap

#### Scenario: Open the RBP Review panel with catalog defaults
- **WHEN** the user opens the data Review RBP panel without changing its dynamic policy values
- **THEN** the desktop request uses a 10-second dynamic analysis window and a 1-second refresh step
- **AND** the RBP calculation retains its declared four-second Welch segment and 50% overlap
