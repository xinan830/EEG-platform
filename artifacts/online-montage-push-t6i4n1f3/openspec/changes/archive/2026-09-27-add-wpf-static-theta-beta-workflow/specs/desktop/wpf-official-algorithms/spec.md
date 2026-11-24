# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static Theta/Beta using a registered channel

WPF SHALL allow Theta/Beta in static mode and SHALL submit one registered
channel, exact time range, algorithm ID, and scientific version.

#### Scenario: Valid Theta/Beta request

- **WHEN** the operator selects Theta/Beta, a registered channel, and a valid
  static time range
- **THEN** WPF submits those exact inputs to one official Run

### Requirement: Theta/Beta displays backend-owned scalar output

WPF SHALL display the backend-returned ratio, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL preserve a
backend unavailable result and SHALL not calculate IAPF or the ratio in the
client.

#### Scenario: Completed or unavailable Theta/Beta result

- **WHEN** the official Run completes with a scalar ratio or structured quality
  failure
- **THEN** WPF displays the backend value or unavailable state without
  converting null to zero
