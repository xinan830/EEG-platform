# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static IAPF using a registered channel

WPF SHALL allow IAPF in static mode and SHALL submit one registered channel,
exact time range, algorithm ID, and scientific version. It SHALL reject an
unregistered or missing channel before submission.

#### Scenario: Valid IAPF request

- **WHEN** the operator selects IAPF, a registered channel, and a valid static
  time range
- **THEN** WPF submits those exact inputs to one official IAPF Run

### Requirement: IAPF displays backend-owned scalar output

WPF SHALL display the backend-returned IAPF value in Hz, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate or
interpolate IAPF in the client.

#### Scenario: Completed IAPF result

- **WHEN** the official IAPF Run completes with a scalar result or structured
  unavailable reason
- **THEN** WPF displays the backend value or unavailable state without
  converting null to zero
