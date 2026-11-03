## ADDED Requirements

### Requirement: Keep RBP topomap color scales stable during playback
Relative-power maps SHALL use the fixed domain `0–1` with unit `ratio`. Absolute-power maps SHALL establish their display bounds from the first ready window for each RBP run and frequency band, then retain those bounds for the remainder of that run. Power values outside the absolute display bounds SHALL saturate at the corresponding end color; displayed endpoints SHALL remain the locked bounds. These display bounds SHALL NOT alter backend values, scientific units, or quality state.

#### Scenario: Relative power remains comparable between windows
- **WHEN** two relative-power windows for the same or different bands are rendered
- **THEN** both use the fixed `0–1 ratio` color domain

#### Scenario: Absolute power locks bounds for one run and band
- **WHEN** the first ready absolute-power window for an RBP run and band is rendered
- **THEN** its finite grid minimum and maximum establish the color bounds
- **AND THEN** later windows for that run and band retain the same bounds

#### Scenario: A different run receives independent bounds
- **WHEN** a new RBP run is rendered for the same recording and band
- **THEN** its first ready absolute-power window establishes new bounds

#### Scenario: A value exceeds locked absolute bounds
- **WHEN** a later grid value is below or above the locked bounds
- **THEN** its color is clamped to the lower or upper endpoint color
- **AND THEN** the underlying EEG result value remains unchanged
