## ADDED Requirements

### Requirement: Format custom-frequency table metrics to two decimal places
The custom-frequency table SHALL display RMS amplitude in µV, absolute power in µV², relative power in percent and PSD density in µV²/Hz with exactly two decimal places. Formatting SHALL preserve underlying precision, numeric sorting, source-window identity, channel order, quality and provenance. Unavailable values SHALL retain their unavailable marker.

#### Scenario: Display a valid metric
- **WHEN** a channel has a valid custom-frequency table metric
- **THEN** its cell displays the rounded value with exactly two decimal places, including trailing zeros, without modifying the source value

#### Scenario: Display an unavailable metric
- **WHEN** a channel metric is unavailable
- **THEN** the cell displays `—` rather than a numeric zero
