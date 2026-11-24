# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF submits static RBP using backend-declared inputs

WPF SHALL allow a runnable catalog RBP item in static mode only after a completed
recording is registered. It SHALL submit the selected backend channel, exact
finite in-record start/end seconds, algorithm ID, scientific version, and
`mode=static`. It SHALL not submit dynamic RBP through this form.

#### Scenario: Valid RBP Run

- **WHEN** the operator selects RBP, a registered channel, and a valid range
- **THEN** WPF creates one official Run with those exact inputs
- **AND** WPF retains the Run identity and backend provenance

### Requirement: RBP is displayed as backend-owned band shares

WPF SHALL display backend-returned Delta, Theta, Alpha, and Beta ratio values and
the `ratio` unit. Null values, failed quality, and unavailable results SHALL
remain explicitly unavailable. WPF SHALL not request a matrix preview or
recompute band power.

#### Scenario: Completed RBP result

- **WHEN** the backend returns a completed RBP metric with four band values
- **THEN** WPF displays all four values and the quality/provenance text
- **AND** no PSD curve or STFT bitmap is shown as the RBP result
