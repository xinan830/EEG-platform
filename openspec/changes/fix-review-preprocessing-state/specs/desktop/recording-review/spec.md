## ADDED Requirements

### Requirement: ICA review confirmation is atomic and bound to scientific inputs
The desktop SHALL keep pending ICA inspection separate from confirmed model and component selections. Changes to montage, bad-channel selection or fitting parameters SHALL invalidate pending and confirmed ICA state. Stale asynchronous responses SHALL NOT restore invalid state.

#### Scenario: A second ICA review is cancelled
- **WHEN** a user cancels a new review after confirming a previous model
- **THEN** the confirmed fingerprint and excluded components SHALL remain unchanged.

#### Scenario: Inputs change or reset while inspection is running
- **WHEN** montage, preprocessing inputs or reset change during inspection
- **THEN** the old response SHALL NOT update current cards, configuration or status.

### Requirement: Preprocessing previews identify current verified data
Previews SHALL represent the current configuration, montage and absolute viewport. Failure, input invalidation, playback or reset SHALL clear obsolete previews. Treatment selection SHALL NOT label original samples as successfully repaired. STFT and TNK SHALL expose that bounded analysis omits component removal.

#### Scenario: Replacement preview fails
- **WHEN** a preview requested under changed settings fails
- **THEN** an obsolete cleaned trace SHALL NOT remain active.

#### Scenario: Interpolation is selected before processed data arrives
- **WHEN** interpolation is selected without a valid processed preview
- **THEN** labels SHALL describe a selected strategy and original traces SHALL retain their original styling.

### Requirement: Preview compression preserves quality and spatial meaning
Preview compression SHALL preserve extrema and explicit unavailable intervals with absolute-second coordinates and microvolt values, declaring its display method. ICA scalp maps SHALL exclude derived channels without a supported spatial rule.

#### Scenario: A narrow gap falls between uniform display points
- **WHEN** a compressed preview spans that gap
- **THEN** its trace SHALL contain an explicit break rather than joining clean samples across it.

#### Scenario: A bipolar channel has no single scalp site
- **WHEN** ICA review includes Fp1-F7
- **THEN** the scalp map SHALL NOT place it at Fp1.

### Requirement: Inspection conclusions expose actual coverage
Bad-channel and bad-segment summaries SHALL identify the returned inspection range. An inspection of only part of a recording SHALL NOT claim the entire recording was checked.

#### Scenario: A long recording is partially inspected
- **WHEN** the requested or resource-limited scan covers only the first part
- **THEN** the summary SHALL display that actual range and scope its clean result to that range.

### Requirement: Unchanged inspections preserve scientific configuration
Read-only rescans SHALL preserve confirmed ICA, selected components and valid previews when effective scientific inputs do not change. An ICA section never inspected SHALL NOT claim a repeated review is needed.

#### Scenario: Bad segments are rescanned under an existing rejection policy
- **WHEN** the scan returns a refreshed report without changing policy or fitting inputs
- **THEN** the confirmed ICA and preview configuration SHALL remain unchanged.

### Requirement: Temporary display montages suspend analysis overlays
Entering or replacing a temporary waveform montage SHALL immediately clear and cancel scientific previews without changing analysis selection or confirmed ICA. Previews SHALL resume only after returning to a compatible ordinary display and pausing playback.

#### Scenario: A preview completes during temporary montage selection
- **WHEN** an earlier preview returns after a temporary display transition starts
- **THEN** it SHALL NOT publish and SHALL NOT dim the temporary waveform.

### Requirement: Derived interpolation labels describe every affected input
Interpolated output metadata SHALL include outputs dependent on an interpolated positive, negative or average-reference input, resolved from formula provenance.

#### Scenario: An interpolated electrode is a bipolar negative input
- **WHEN** C4 is interpolated before computing F4-C4 and C4-P4
- **THEN** both derived outputs SHALL be identified as affected by interpolation.

### Requirement: ICA mini waveforms retain diagnostic display features
ICA previews SHALL use extrema envelopes with explicit second coordinates, dimensionless normalized values and explicit gaps. Desktop rendering SHALL preserve the supplied nonuniform timing.

#### Scenario: A narrow pulse lies between uniform display points
- **WHEN** a 10-second ICA waveform is compressed for a card
- **THEN** its pulse extrema SHALL remain visible at the original times without joining across gaps.
