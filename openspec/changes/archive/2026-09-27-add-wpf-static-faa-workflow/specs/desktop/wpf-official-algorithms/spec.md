# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static FAA with two distinct source channels

WPF SHALL allow FAA only in static mode and SHALL submit the registered F3
source channel, registered F4 source channel, exact time range, algorithm ID,
and scientific version. It SHALL reject missing, unknown, or identical source
channels before submission.

#### Scenario: Valid FAA request

- **WHEN** the operator selects FAA, two different registered channels, and a
  valid static time range
- **THEN** WPF submits both source channels to one official FAA Run

#### Scenario: Invalid FAA channel selection

- **WHEN** either channel is missing/unknown or both selectors contain the same
  channel
- **THEN** WPF refuses submission with a specific validation message

### Requirement: FAA displays backend-owned scalar output

WPF SHALL display the backend-returned FAA value, unit, quality, source
channels, requested/actual range, Run identity, and provenance. It SHALL not
recompute Alpha power or FAA in the client.

#### Scenario: Completed FAA result

- **WHEN** the official FAA Run completes with a scalar result
- **THEN** WPF displays the backend value and provenance without client-side
  recomputation
