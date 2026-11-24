# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static Band Ratio with two explicit frequency bands

WPF SHALL allow Band Ratio in static mode and SHALL submit one registered
channel, exact time range, numerator and denominator band edges, algorithm ID,
and scientific version. It SHALL reject invalid ordering and Nyquist bounds
before submission.

#### Scenario: Valid Band Ratio request

- **WHEN** the operator selects Band Ratio, a channel, a valid time range, and
  two valid bands below Nyquist
- **THEN** WPF submits those exact inputs to one official Run

#### Scenario: Invalid Band Ratio bands

- **WHEN** either band is missing, nonfinite, negative, reversed, equal, or has
  an upper edge at or above Nyquist
- **THEN** WPF refuses submission with a specific validation message

### Requirement: Band Ratio displays backend-owned scalar output

WPF SHALL display the backend-returned ratio, unit, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate
the ratio in the client or request a matrix preview.

#### Scenario: Completed Band Ratio result

- **WHEN** the official Run completes with a scalar ratio output
- **THEN** WPF displays the backend value and provenance without client-side
  recomputation
