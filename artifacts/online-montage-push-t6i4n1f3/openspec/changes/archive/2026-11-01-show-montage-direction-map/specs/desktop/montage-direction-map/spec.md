## ADDED Requirements

### Requirement: Preview montage direction on the electrode map
The desktop client SHALL provide a floating direction-map view from the montage configuration page using the current draft montage rows and selected channel-configuration snapshot.

#### Scenario: Original hardware reference
- **WHEN** a draft output is configured as F3-REF and the operator opens the direction map
- **THEN** the map draws an arrow from F3 to REF

#### Scenario: Channel and grouped references
- **WHEN** an output references another channel, an average group, or a specified pair
- **THEN** the map draws an arrow from the positive electrode to each negative-reference member that has a valid diagram position

#### Scenario: Unavailable position
- **WHEN** a formula endpoint has no valid diagram position
- **THEN** the endpoint relationship is omitted from the map and the view explains that unresolved positions are not drawn

#### Scenario: Display-only semantics
- **WHEN** the operator views montage directions
- **THEN** the map displays no EEG values and does not change signal or analysis reference calculations
