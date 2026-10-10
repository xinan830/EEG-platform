## ADDED Requirements

### Requirement: Montage details expose saved device input numbers
The desktop SHALL display positive and reference input numbers separately from
electrode labels using the montage's bound channel snapshot. Display order and
current presets SHALL NOT determine these numbers or alter signal calculations.

#### Scenario: Renamed electrode
- **WHEN** the saved snapshot maps native input 3 to F88
- **THEN** F88 appears separately from device input number 3

#### Scenario: Reference sources
- **WHEN** a row uses a channel, specified pair or average reference
- **THEN** its reference number column lists the actual selected source inputs
- **AND** changing the reference selection updates the number presentation

#### Scenario: No verifiable sampled input
- **WHEN** the row uses hardware REF
- **THEN** the reference number column displays a dash, not an invented number
- **WHEN** a source is missing or ambiguously mapped
- **THEN** its input number displays unknown
