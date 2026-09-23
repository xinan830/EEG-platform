## ADDED Requirements

### Requirement: Persist screen calibration inputs across application restarts

The desktop SHALL persist valid physical display width and height inputs in
centimeters for the current workstation user. On startup or when the owning
display context becomes available, the calibration page SHALL restore the
saved values and use them for millimeters-per-DIP conversion. Incomplete or
invalid drafts SHALL NOT replace the last valid persisted calibration.

#### Scenario: Valid calibration survives restart

- **WHEN** an operator enters valid width and height values and saves or leaves the calibration page
- **THEN** the values SHALL be written to the local calibration store
- **AND** a new application instance SHALL display the same values for the matching display context
- **AND** the derived conversion SHALL use the restored physical dimensions rather than nominal 96-DPI assumptions.

#### Scenario: Invalid draft does not erase a valid calibration

- **WHEN** an operator clears one field or enters a value outside the accepted physical range
- **THEN** the draft MAY be shown as invalid
- **AND** the last valid persisted profile SHALL remain unchanged and available for the next startup.

#### Scenario: Display identity is temporarily unavailable

- **WHEN** startup first exposes only a fallback display identity and the final monitor identity is not yet available
- **THEN** the page SHALL retain or restore the most recent valid calibration as a fallback
- **AND** a later exact display match SHALL take precedence without changing the stored physical units.
