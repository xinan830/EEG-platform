## ADDED Requirements

### Requirement: Current official algorithm parameters match executable defaults

For PSD, the official catalog parameter schema SHALL declare the same defaults as the current typed PSD configuration for `low_hz`, `high_hz`, and `notch_hz`. Catalog consumers may use these declared defaults to construct a current-version request without maintaining a second copy. Changing the catalog declaration alone SHALL NOT change the executable configuration defaults.

#### Scenario: Resolve the PSD catalog entry

- **WHEN** a consumer reads the current PSD catalog parameter schema
- **THEN** the defaults are `low_hz=1.0 Hz`, `high_hz=30.0 Hz`, and `notch_hz=50.0 Hz`
- **AND** each equals the corresponding current `PsdConfig` default.

### Requirement: PSD consumers use only the current structured band-share contract

PSD presentation consumers SHALL read band-share values from the current `band_share` result array and SHALL NOT reconstruct them from retired `spectral_evidence.relative_band_power` fields.

#### Scenario: Retired PSD evidence is present without a current band-share array

- **WHEN** a PSD result omits the current `band_share` array but contains retired `spectral_evidence.relative_band_power` evidence
- **THEN** PSD spectrum presentation may render validated current `frequency_hz` and `psd` data
- **AND** band-share presentation remains unavailable
- **AND** the retired evidence is not converted or presented as a current result.
