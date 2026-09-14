# Signal preprocessing baseline

## Purpose

Separate the current viewer filtering behavior from the fixed offline analysis preprocessing contract.

## Requirements

### Requirement: Maintain distinct viewer and analysis pipelines

The system SHALL keep display filtering and offline scientific preprocessing as independently versioned pipelines.

#### Scenario: Change viewer filters

- **WHEN** a user changes low-cut, high-cut, notch, or baseline display settings
- **THEN** waveform rendering changes and the fixed offline spectral preprocessing contract does not

### Requirement: Process offline spectral data continuously

The `offline-spectral-v3` pipeline SHALL filter the complete continuous recording before selecting an analysis time range.

#### Scenario: Analyze adjacent ranges

- **WHEN** two analysis requests select adjacent windows from one recording
- **THEN** both windows are sliced from the same continuous zero-phase preprocessed signal semantics rather than independently filtering each cropped window

### Requirement: Use explicit internal units

The system SHALL represent internal EEG samples as `float64` volts and SHALL convert to microvolt-derived public units only at a documented output boundary.

#### Scenario: Produce PSD output

- **WHEN** the backend computes density in `V^2/Hz`
- **THEN** the API converts once to `uV^2/Hz` and identifies that unit in the response

### Requirement: Reset stateful display filters predictably

The viewer pipeline SHALL rebuild causal filter state after seek, channel, montage, or filter changes according to its playback contract.

#### Scenario: Seek within playback

- **WHEN** playback seeks to an absolute recording time
- **THEN** the display filter state is reconstructed for that location and returned timestamps remain absolute recording time
