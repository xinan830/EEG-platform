# Spec Delta

## ADDED Requirements

### Requirement: Open the project list as the desktop landing view

The desktop shell SHALL present the project list when the application first opens, with the project status filter initially set to “进行中”. The user SHALL retain access to the other project status filters and other workspace views.

#### Scenario: Application starts

- **WHEN** the desktop shell finishes initializing
- **THEN** it SHALL show the project list and select the project-list navigation item
- **AND** the project list SHALL initially filter to projects with status “进行中”

#### Scenario: User changes the project status filter

- **WHEN** the user selects another available status filter
- **THEN** the project list SHALL apply that filter and display the selected filter state

### Requirement: Apply a restrained blue brand palette

The desktop shell and project-management views SHALL use a consistent blue-led brand palette with no more than three brand accent colors. White and neutral grays MAY be used as surfaces and text colors. Semantic status indicators and EEG channel traces MAY use distinct data colors when needed for meaning and legibility; they SHALL NOT be used as additional brand accents.

#### Scenario: Display primary shell actions

- **WHEN** the shell displays navigation, primary actions, or selected states
- **THEN** those elements SHALL use the shared blue brand palette consistently
- **AND** green SHALL NOT be used as a primary brand accent

#### Scenario: Display status indicators or EEG traces

- **WHEN** status indicators or multi-channel EEG traces are displayed
- **THEN** their colors SHALL preserve semantic distinction and contrast
- **AND** those data colors SHALL remain distinct from the shell's brand palette role.
