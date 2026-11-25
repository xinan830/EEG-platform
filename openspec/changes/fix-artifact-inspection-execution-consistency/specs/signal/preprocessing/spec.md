## ADDED Requirements

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
