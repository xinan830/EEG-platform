# STFT preprocessing parameters

## ADDED Requirements

### Requirement: Official STFT accepts shared frequency controls
The official STFT Run MUST accept `low_hz`, `high_hz`, and nullable `notch_hz`,
with defaults of `1`, `50`, and `null` when the WPF shared controls are used.

#### Scenario: STFT configuration is persisted
- **WHEN** a Run is created with an STFT channel and frequency controls
- **THEN** the resolved configuration and execution provenance contain the
  selected range and notch value

### Requirement: STFT applies the selected range
The STFT backend MUST apply the selected range to preprocessing and return only
frequency bins inside the requested output range.

#### Scenario: Custom output range
- **WHEN** an STFT Run requests `8-13 Hz`
- **THEN** its frequency axis is constrained to `8-13 Hz` and its provenance
  records that range

### Requirement: Invalid Nyquist requests are rejected
The backend MUST reject a high frequency at or above the recording Nyquist
frequency with a structured analysis error.

#### Scenario: 50 Hz request on 100 Hz data
- **WHEN** STFT requests a 50 Hz high edge for a 100 Hz recording
- **THEN** the Run is unavailable/failed with an explicit Nyquist reason

### Requirement: Legacy spectrogram defaults remain stable
The non-official spectrogram service MUST retain its historical 1-30 Hz default
when callers do not provide explicit frequency controls.

#### Scenario: Unconfigured spectrogram keeps the historical range
- **WHEN** a legacy spectrogram caller omits frequency controls
- **THEN** the returned frequency axis remains within `1-30 Hz`
