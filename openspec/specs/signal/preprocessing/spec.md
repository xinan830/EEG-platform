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

### Requirement: Share authoritative artifact detections across offline consumers

For identical recording, effective montage, ordered detection channels, scientific filters and artifact configuration, inspection, scientific preview and algorithm quality control SHALL consume the same full-recording pre-removal detection report. Automatic detections SHALL NOT be converted into manual annotations.

#### Scenario: Inspect then reject a channel-specific segment

- **WHEN** inspection lists an interval for a channel and the same scientific configuration is applied with reject policy
- **THEN** every sample in that half-open interval for that channel is masked as NaN, without masking unrelated clean channels or changing recording time

#### Scenario: Inspect a partial recording range

- **WHEN** inspection requests a bounded subrange
- **THEN** returned intervals are clipped projections of the full-recording report and identify their detection scope and scientific filters

#### Scenario: Gate a single-channel finite-context algorithm

- **WHEN** a bounded algorithm applies artifact QC to one channel
- **THEN** it uses the authoritative report projected to its target range and channels while preserving its declared finite-context signal filtering

#### Scenario: Change detection inputs

- **WHEN** thresholds, bad-channel treatment or montage change
- **THEN** previous UI scan markers become unavailable until rescanned and cached reports from different detection inputs are not reused

#### Scenario: Leave artifact QC disabled

- **WHEN** a bounded analysis does not enable artifact QC
- **THEN** its existing bounded I/O and scientific output remain unchanged
### Requirement: Evaluate every sample-aligned amplitude window

Offline amplitude peak-to-peak quality control SHALL evaluate every complete sample-aligned window of its configured width, without skipping window origins. It SHALL mark the per-channel union of windows whose finite-value peak-to-peak strictly exceeds the configured threshold, then apply the declared minimum duration and padding. Non-finite samples SHALL remain unavailable, channel masks SHALL remain independent, and intervals SHALL retain absolute half-open sample coordinates.

#### Scenario: Extrema straddle the former stride grid

- **WHEN** a valid half-second window exceeds 300 uV but neighboring 125 ms-spaced windows do not
- **THEN** the affected channel is still detected, regardless of its offset relative to that former grid

#### Scenario: Short or boundary windows

- **WHEN** a recording is shorter than the configured width or an exceeded window touches either recording edge
- **THEN** QC evaluates the available whole short recording or the complete edge-aligned window without fabricating outside samples

#### Scenario: Revise detection semantics

- **WHEN** the exact rolling detector replaces the skipped-origin detector
- **THEN** revised QC identities invalidate reuse of older prepared signals, algorithm results and ICA confirmations, and inspection, preview and bounded gates continue to share the revised authoritative report

### Requirement: Distinguish transform unavailability from detected artifacts

Applied offline artifact removal SHALL report absolute half-open intervals of unavailable output, identifying missing input channels and additionally unavailable output channels separately from authoritative artifact detections. Preview and algorithm evidence SHALL share this application validity. No missing input SHALL be replaced with zeros or untreated raw samples represented as cleaned.

#### Scenario: A local gap prevents multichannel ICA reconstruction

- **WHEN** one ICA input channel is unavailable for an interval and selected components are removed
- **THEN** unavailable reconstructed outputs retain gaps with missing-input provenance, while the detector report continues to identify only its original affected channels

#### Scenario: No ICA components are excluded

- **WHEN** the reviewed exclusion set is empty
- **THEN** signal values and per-channel missing masks remain unchanged without additional reconstruction gaps

#### Scenario: Window evidence and failed spectral gates

- **WHEN** a preview or algorithm window overlaps a reconstruction gap
- **THEN** its evidence reports the clipped absolute interval and affected channels, and failed quality gates preserve this explanation

#### Scenario: Cache semantics change without refitting

- **WHEN** application validity evidence is introduced
- **THEN** active removal result identities change while the unchanged reviewed ICA fit identity remains stable
