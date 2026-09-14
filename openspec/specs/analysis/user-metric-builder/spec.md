# analysis/user-metric-builder Specification

## Purpose
TBD - created by archiving change add-research-metric-formula-builder. Update Purpose after archive.
## Requirements
### Requirement: Compose a metric from curated backend features

The user workbench SHALL allow selecting named Delta, Theta, Alpha, or Beta
absolute/relative power features, selecting an approved binary operation, and
assigning a user-readable output name.

#### Scenario: Configure Theta/Beta ratio
- **WHEN** the user selects Theta 功率, Beta 功率, and division
- **THEN** the form shows `Theta 功率 ÷ Beta 功率` and a named output
- **AND THEN** the browser does not calculate a value.

### Requirement: Validate and persist through the backend

The builder SHALL submit a canonical definition draft to backend validation
before creating a definition and immutable version.

#### Scenario: Save a valid metric
- **WHEN** the backend accepts the generated draft
- **THEN** the workbench creates a definition and version through the existing API
- **AND THEN** the saved definition retains feature identifiers, units, and the
  `offline-spectral-v3` reference.

### Requirement: Do not fabricate EEG output

The builder SHALL state that saving a formula is not the same as executing it.
It SHALL NOT display a calculated EEG result until a backend execution resolves
the selected features for a recording and range.

#### Scenario: Formula is saved without execution
- **WHEN** a user completes the save flow
- **THEN** the UI reports that the definition was saved
- **AND THEN** it does not present a fake numeric EEG result.

