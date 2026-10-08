## ADDED Requirements

### Requirement: Load bounded long-recording PSD previews

The WPF PSD detail loader SHALL request the existing structured preview API
with a 1,000,000-cell budget. Successful previews SHALL preserve backend
spectrum values, band shares and dynamic windows without recomputation.

#### Scenario: Preview exceeds the old desktop budget

- **WHEN** a completed dynamic PSD contains more than 20,000 and at most
  1,000,000 numeric cells
- **THEN** the desktop loads its spectrum, backend band shares and timeline
- **AND** seeking selects the corresponding backend row without rerunning
  the scientific calculation

#### Scenario: Static band share output

- **WHEN** a static PSD preview contains five available band-share values
- **THEN** the desktop exposes those five values to the band-share chart

#### Scenario: Rejected dynamic window

- **WHEN** the user seeks to a rejected window
- **THEN** the spectrum and band-share projection do not retain the previous
  valid window's values
