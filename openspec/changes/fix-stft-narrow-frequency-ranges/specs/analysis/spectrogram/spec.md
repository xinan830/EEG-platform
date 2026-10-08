## ADDED Requirements

### Requirement: Supplementary bands must not invalidate a valid output range
The spectrogram service SHALL compute supplementary standard-band integrals on
the uncropped grid of the selected filtered signal before masking the returned
matrix to the requested frequency range. It SHALL return null for bands whose
full edges are not supported by the recording frequency grid, without clipping
or extrapolating their edges.

#### Scenario: Request an Alpha-only STFT
- **WHEN** a static or dynamic Run requests 8-13 Hz with valid input quality
- **THEN** it completes with frequencies confined to 8-13 Hz
- **AND** supplementary Delta, Theta or Beta integration does not fail the Run

#### Scenario: Recording cannot support a standard band
- **WHEN** recording Nyquist is below the full Beta band but the requested
  output range is valid
- **THEN** that supplementary band is unavailable rather than zero or a Run error

### Requirement: Dynamic failure axes match the requested STFT range
The STFT module SHALL build exception-path frequency axes with the same frame
length and requested frequency bounds as successful windows.

#### Scenario: Initial quality failure followed by a clean window
- **WHEN** the first 8-13 Hz dynamic window fails the quality gate and a later
  window succeeds
- **THEN** both windows use the same frequency grid, rejected values remain
  unavailable, and the later matrix is preserved
