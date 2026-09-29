## ADDED Requirements

### Requirement: PSD filter controls are independent from trend display range
The PSD detail configuration MUST send high-pass, low-pass, and notch values as
filter parameters. The dynamic trend display range MUST remain a display
contract and MUST NOT overwrite those filter values.

#### Scenario: Dynamic PSD preserves configured filters
- **GIVEN** a user selects high-pass, low-pass, and notch values in the PSD
  detail page
- **WHEN** WPF submits a dynamic PSD Run
- **THEN** it sends the selected filter values unchanged
- **AND** the backend independently returns the fixed 1-50 Hz trend axis
