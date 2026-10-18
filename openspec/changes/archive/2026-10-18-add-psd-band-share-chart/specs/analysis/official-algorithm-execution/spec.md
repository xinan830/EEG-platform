## ADDED Requirements

### Requirement: PSD exposes standard band shares in structured results
The official PSD structured result MUST expose `band_share` in the fixed order
Delta, Theta, Alpha, Beta, Gamma, with unit `ratio`. Static output MUST use
five values and dynamic output MUST use one five-value row per analysis window.

#### Scenario: Render band shares without client recomputation
- **WHEN** a clean PSD run completes
- **THEN** the structured preview contains `band_share`
- **AND** its values are backend-computed relative powers
- **AND** the calculation trace declares the band names and ranges
