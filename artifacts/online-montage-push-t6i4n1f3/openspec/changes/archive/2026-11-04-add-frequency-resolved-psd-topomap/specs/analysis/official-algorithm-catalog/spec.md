## ADDED Requirements

### Requirement: Official PSD publishes ordered channel-resolved spectra
The current official PSD contract SHALL accept a nonempty, unique, explicitly ordered set of analysis channels and SHALL calculate each channel with the same declared Welch, filter, window, and preprocessing configuration. Results SHALL expose the exact requested `channel_order` and one shared, strictly increasing `frequency_hz` axis. Static `psd` values SHALL have shape `[channel, frequency]`; dynamic values SHALL have shape `[window, channel, frequency]`. Derived `band_share` values SHALL preserve the corresponding channel and window axes. Each channel in each window SHALL have its own quality state and unavailable reason. A rejected or unavailable channel SHALL NOT invalidate valid channels in the same window, and unavailable values SHALL NOT be replaced by zero, a neighboring channel, or a previous window.

#### Scenario: Run PSD for an ordered channel set
- **WHEN** a static or dynamic PSD request supplies a unique ordered channel set
- **THEN** the result returns the same channel order, one shared frequency axis, and the declared static or dynamic array shape
- **AND** each channel uses the same scientific configuration and actual window boundaries

#### Scenario: One channel fails its quality gate
- **WHEN** one channel fails the PSD quality gate while other requested channels pass for the same dynamic window
- **THEN** the failed channel has a channel-local rejected state and reason
- **AND** the other channels retain their valid PSD values and states

#### Scenario: Requested channel set is invalid
- **WHEN** a PSD request contains a duplicate, unknown, or empty channel set
- **THEN** the request fails with a structured input error
- **AND** the runtime does not reorder channels or guess missing channels

#### Scenario: Channel frequency axes disagree
- **WHEN** channel calculations for one result do not produce the same declared frequency axis
- **THEN** PSD execution fails with a structured contract error
- **AND** the result does not publish a misaligned matrix

### Requirement: Standardize shared band integration without flattening algorithm parameters
The system SHALL use `scientific/primitives/spectral.py::band_power` for PSD band shares and offline Brainbeat band powers. The primitive SHALL interpolate PSD values at exact band edges and integrate with the trapezoidal rule. Standardizing integration SHALL NOT force algorithms to share frequency bands, denominators, filter settings, or Welch window lengths. Brainbeat SHALL retain its IAPF-derived Theta and Alpha ranges and 1–30 Hz denominator; its persisted spectral metadata SHALL match the four-second Welch segment used by the shared spectral source.

#### Scenario: Compare band power under identical parameters
- **WHEN** PSD and Brainbeat integrate the same PSD values over the same band
- **THEN** both return the same shared primitive result within floating-point tolerance
- **AND** each algorithm retains its own configured band definitions and denominator

#### Scenario: Brainbeat Welch metadata matches execution
- **WHEN** a current Brainbeat Run stores its spectral definition
- **THEN** the persisted Welch segment length is four seconds and overlap is 50 percent
- **AND** the samples, filters, and window policy used for the result remain unchanged
