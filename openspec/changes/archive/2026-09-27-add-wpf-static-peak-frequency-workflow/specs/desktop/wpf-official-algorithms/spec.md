# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static Peak Frequency with an explicit frequency band

WPF SHALL allow Peak Frequency only in static mode and SHALL submit one
registered backend channel, exact time range, `low_hz`, `high_hz`, algorithm ID,
and scientific version. It SHALL reject invalid band values before submission.

#### Scenario: Valid Peak Frequency request

- **WHEN** the operator selects Peak Frequency, a channel, a valid time range,
  and `low_hz < high_hz < Nyquist`
- **THEN** WPF submits those exact inputs to one official Run

#### Scenario: Invalid frequency band

- **WHEN** either edge is missing/nonfinite/negative, the edges are reversed or
  equal, or the high edge is at or above Nyquist
- **THEN** WPF refuses submission with a specific validation message

### Requirement: Peak Frequency displays backend-owned scalar output

WPF SHALL display the backend-returned peak frequency in Hz, quality, channel,
requested/actual range, Run identity, and provenance. It SHALL not calculate,
interpolate, or request a matrix preview for this scalar result.

#### Scenario: Completed Peak Frequency result

- **WHEN** the official Run completes with a scalar `peak_frequency_hz` output
- **THEN** WPF displays the backend value and its Hz unit together with the
  returned quality, channel, requested/actual range, Run identity, and
  provenance, without requesting a structured matrix preview
