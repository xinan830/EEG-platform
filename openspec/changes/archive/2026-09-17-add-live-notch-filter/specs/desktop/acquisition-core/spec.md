## MODIFIED Requirements

### Requirement: Live display filtering is backend-owned

The desktop SHALL preserve raw incoming batches before dispatching a bounded
copy to the local scientific backend for display filtering. The backend SHALL
return float64 V values and maintain filter state per live session. The desktop
SHALL use returned filtered batches for waveform display and SHALL not
implement an independent high-pass, low-pass, or notch formula. A live session
MAY configure no notch, a 50 Hz notch, or a 60 Hz notch; an unsupported notch
frequency SHALL be rejected. The notch SHALL affect only EEG reference and
bipolar display columns, never persisted raw values, trigger values, or sample
counters.

#### Scenario: A live filter session processes a batch

- **WHEN** the desktop submits an accepted acquisition batch
- **THEN** only EEG reference and bipolar columns SHALL be filtered
- **AND** counter and trigger values SHALL remain unchanged
- **AND** raw recording persistence SHALL remain unfiltered

#### Scenario: The user enables 50 Hz display suppression

- **WHEN** a live session is configured with a 50 Hz notch
- **THEN** the returned EEG display values SHALL be processed by that session's
  stateful 50 Hz notch before its configured display band-pass
- **AND** the input raw batch and persisted raw recording SHALL remain in V
  without that notch applied

### Requirement: Stateful filter configuration does not mix histories

The desktop SHALL validate high-pass, low-pass, and notch settings before
stream opening. It SHALL reject a low-pass or enabled notch at or above the
actual Nyquist frequency and SHALL not change filter coefficients during a
recording.

#### Scenario: User attempts to change a filter during recording

- **WHEN** a recording is active
- **THEN** the high-pass, low-pass, and notch controls SHALL be unavailable
