## ADDED Requirements

### Requirement: Compare engineering evidence pointwise

The system SHALL compare finite expected and actual values under explicit
absolute and relative tolerances, persist a ValidationRun, and return pointwise
summary evidence. It SHALL distinguish unavailable values from numeric zero.

#### Scenario: Record a finite parity comparison
- **WHEN** expected and actual PSD points are within configured tolerances
- **THEN** the ValidationRun reports maximum errors, pass rate, and engineering
  scope without claiming clinical validity
