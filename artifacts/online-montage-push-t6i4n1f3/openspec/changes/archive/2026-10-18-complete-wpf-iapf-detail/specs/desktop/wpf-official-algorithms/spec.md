## ADDED Requirements

### Requirement: WPF IAPF detail presents backend-owned result and method
The IAPF detail page SHALL display the backend IAPF value in Hz and the selected Peak/COG method evidence. It SHALL show unavailable results without substituting zero or a prior window value. It SHALL NOT calculate IAPF or draw a fitting curve absent from backend artifacts.

#### Scenario: Static result and unavailable result
- **WHEN** an IAPF Run completes with a finite scalar and method evidence
- **THEN** WPF displays that value, unit, source method, channel and Run context
- **WHEN** the result is unavailable
- **THEN** WPF shows the backend failure state without a numeric value

### Requirement: WPF IAPF dynamic trend follows recording time
The IAPF dynamic chart SHALL render only backend-returned complete values at their `time_s` positions, synchronized with the existing seek/playback cursor. Rejected or unavailable windows SHALL remain gaps and retain their reason in the result panel.

#### Scenario: Seek across valid and rejected windows
- **WHEN** the operator seeks through dynamic IAPF windows
- **THEN** the chart reveals only results at or before the cursor
- **AND** a rejected current window displays no carried-forward IAPF value

### Requirement: IAPF retains the shared quality status card
The IAPF detail page SHALL display the same shared quality status card used by other official spectral algorithms. Its status and failure details SHALL come from the backend IAPF result; dynamic window counts and reasons SHALL reflect windows reached by the current timeline cursor.

#### Scenario: Inspect a rejected IAPF window
- **WHEN** the timeline reaches a rejected IAPF window
- **THEN** the shared card shows its quality state, rejected count and failure reason
- **AND** the operator can expand the window failure details
