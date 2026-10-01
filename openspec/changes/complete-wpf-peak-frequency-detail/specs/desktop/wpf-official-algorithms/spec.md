## ADDED Requirements

### Requirement: WPF Peak Frequency detail displays backend-owned values
The Peak Frequency detail SHALL show the backend value in Hz, source channel, actual analysis window and available peak-power evidence. The client SHALL NOT select a peak from PSD data or replace an unavailable result with zero or a prior result.

#### Scenario: Static and unavailable results
- **WHEN** a static Peak Frequency Run provides a finite result
- **THEN** the detail shows that frequency and its declared unit
- **WHEN** the backend result is unavailable
- **THEN** the detail shows its failure reason with no numeric substitute

### Requirement: Dynamic Peak Frequency follows the shared recording-time cursor
The detail trend SHALL reveal only backend-returned complete points at or before the cursor. Rejected or unavailable windows SHALL leave gaps, retain their backend reasons and update shared quality counts only after the cursor reaches them.

#### Scenario: Seek across a rejected window
- **WHEN** the operator seeks from a complete point into a rejected window
- **THEN** the current frequency becomes unavailable and the trend does not carry the previous value forward
- **AND** the shared quality card exposes the rejected window and its reason

### Requirement: Dynamic scalar points preserve structured failures
The backend SHALL serialize each dynamic scalar window's failure object when present, including its code and message, alongside quality status. Successful points SHALL carry no failure.

#### Scenario: Quality gate rejects a dynamic point
- **WHEN** a dynamic algorithm returns a failed window
- **THEN** the Run result series contains that window's structured failure without fabricating a numeric value
