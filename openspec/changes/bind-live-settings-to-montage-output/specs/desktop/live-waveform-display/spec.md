## ADDED Requirements

### Requirement: Live channel controls represent selected montage outputs

When a live acquisition uses a selected display montage, the live settings
popup SHALL show that montage's derived output count and names rather than a
static physical-input count or physical input-label checklist. Selecting an
output in the popup SHALL control only whether that derived output is rendered
in the active live session. It SHALL NOT alter the montage formula, montage
order, source channel configuration, raw acquisition values, raw persistence,
or scientific analysis reference.

#### Scenario: A bipolar montage has a subset of source channels

- **WHEN** the selected montage derives a subset of its source configuration,
  such as a bipolar chain
- **THEN** the popup SHALL report and list only that derived subset
- **AND** disabling one listed item SHALL remove only its live waveform trace

#### Scenario: No montage is selected

- **WHEN** the live settings popup is opened before a montage is selected
- **THEN** it SHALL report that no montage is selected
- **AND** it SHALL NOT fabricate a standard 10-20 channel count or reference mode
