## ADDED Requirements

### Requirement: Run independent PSD reference validation from the workbench

The frontend SHALL display backend-returned contract details and SHALL NOT
recompute spectral values. It SHALL allow a user to start an independent PSD
reference check for the active analysis range and current channel order, then
render only the returned engineering validation result.

#### Scenario: Run an independent PSD check from the workbench
- **WHEN** the user starts an independent PSD check with a valid active range
- **THEN** the browser SHALL submit the range and channel order to the backend
- **AND THEN** display PASS/failure, point count, maximum errors and the
  backend-defined engineering-only scope without calculating PSD locally.
