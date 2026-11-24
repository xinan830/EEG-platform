## ADDED Requirements

### Requirement: Live display filtering is backend-owned

The desktop SHALL preserve raw incoming batches before dispatching a bounded
copy to the local scientific backend for display filtering. The backend SHALL
return float64 V values and maintain filter state per live session. The desktop
SHALL use returned filtered batches for waveform display and SHALL not
implement an independent high-pass or low-pass formula.

#### Scenario: A live filter session processes a batch

- **WHEN** the desktop submits an accepted acquisition batch
- **THEN** only EEG reference and bipolar columns SHALL be filtered
- **AND** counter and trigger values SHALL remain unchanged
- **AND** raw recording persistence SHALL remain unfiltered

### Requirement: Stateful filter configuration does not mix histories

The desktop SHALL validate high-pass and low-pass settings before stream
opening. It SHALL reject a low-pass at or above the actual Nyquist frequency
and SHALL not change filter coefficients during a recording.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording is active
- **THEN** the high-pass and low-pass controls SHALL be unavailable
