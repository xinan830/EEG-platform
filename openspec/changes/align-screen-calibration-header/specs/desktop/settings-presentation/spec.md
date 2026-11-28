## ADDED Requirements

### Requirement: Screen calibration uses the shared settings header
The screen calibration settings page SHALL use the shared back-navigation icon button beside its title and description. The title SHALL use 20-point text and the description SHALL use 13-point text, with page margins matching the settings pages. Calibration and navigation behavior SHALL remain unchanged.

#### Scenario: Open screen calibration from settings
- **WHEN** the user opens screen calibration
- **THEN** the header displays the shared back-navigation icon beside the title and description
- **AND** returning to settings uses the existing navigation handler
- **AND** calibration input, physical units, persistence and save actions remain unchanged
