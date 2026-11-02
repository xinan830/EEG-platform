## ADDED Requirements

### Requirement: Shared instrument workspace presentation

Acquisition and review SHALL share an opt-in compact presentation with menus
in the session caption, grouped tools, labeled waveform parameters, and a
dominant waveform. Presentation SHALL NOT change scientific settings, time,
units, channel order, provenance, unavailable results or session state machines.

#### Scenario: Review menus are hosted in the caption
- **WHEN** review is opened in its session window
- **THEN** project and analysis menu actions appear in the caption using review's own DataContext
- **AND** no separate page menu row takes waveform height

#### Scenario: Narrow parameter strip
- **WHEN** the parameter content exceeds the available workspace width
- **THEN** horizontal scrolling makes every existing control reachable without overlapping neighboring controls

#### Scenario: Acquisition controls remain state dependent
- **WHEN** acquisition is previewing, recording or paused
- **THEN** start, pause, resume and finish controls retain their existing commands and permissions

#### Scenario: Closing the instrument window
- **WHEN** the user activates the caption close button
- **THEN** the existing confirmation and session cleanup path executes before the window closes

#### Scenario: Unrelated workspaces
- **WHEN** another page uses the original workspace style
- **THEN** its presentation remains unchanged
