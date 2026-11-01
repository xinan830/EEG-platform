# desktop/stft-review-panel Specification

## Purpose

STFT Review displays validated time-frequency windows from the active recording and shared review cursor, while keeping waveform playback independent and avoiding a second reader or clock.

## Requirements

### Requirement: Review STFT consumes the workspace recording and cursor
Review SHALL offer STFT alongside RBP as a selectable algorithm panel. Only one panel SHALL be active. It SHALL consume the registered recording, source identity, selected compatible montage and recording-relative sample cursor from Review, using the unified declared dynamic-window execution channel. It SHALL NOT create an independent playback clock, recording reader, full Run per cursor update or whole-record preprocessing barrier.

#### Scenario: Open STFT during playback
- **WHEN** the user selects STFT in the recording Review algorithm menu
- **THEN** the panel requests the backend-planned window for the existing cursor
- **AND** waveform playback retains its recording and clock.

#### Scenario: Switch or close a panel while a request is pending
- **WHEN** the user switches between RBP and STFT or closes the active panel
- **THEN** old preparation and subscriptions are released
- **AND** late results cannot replace the active panel, restart playback or attach a closed panel.

### Requirement: STFT Review preserves the scientific result contract
The panel SHALL select the current runnable STFT catalog version and its declared window/refresh policy. It SHALL serialize the selected channel and montage snapshot. The selector SHALL preserve the declared order of all available channels, while submitted montage output mappings SHALL contain exactly the selected input with its registered role, id and unit and unchanged montage identity. It SHALL use the bounded scientific preprocessing contract independent of display filters. It SHALL validate output kind, quality, matrix shape, axis units, linear V^2/Hz and dB re 1 uV^2/Hz units and bounded preprocessing provenance. Relative inner centers SHALL be projected to recording-absolute seconds exactly once using the returned actual range.

#### Scenario: Select one input from a multi-output montage
- **WHEN** STFT first opens or the user changes its channel or montage
- **THEN** the request contains exactly one montage output mapping matching the selected channel
- **AND** the registered role, unit and montage identity are preserved without removing other available channels from the selector.

#### Scenario: Change channel at a paused cursor
- **WHEN** the user selects another available STFT channel
- **THEN** a new immutable scientific configuration is submitted at the same Review cursor
- **AND** the previous channel image is cleared before the replacement result is displayed.

#### Scenario: Display a returned STFT window
- **WHEN** a clean current-format time-frequency result arrives for the active snapshot
- **THEN** the compact heatmap displays the backend matrix with frequency ticks and recording-time centers
- **AND** it shows tick values without axis titles and keeps the plot area close to both tick rails
- **AND** new time columns accumulate to the right while overlapping times retain their first displayed backend estimate without duplication.

#### Scenario: Navigate accumulated history
- **WHEN** the cursor jumps forward beyond the displayed data
- **THEN** only returned columns are appended, leaving the unvisited gap unfilled
- **AND** backward windows, configuration resets and panel closure clear the accumulated image before displaying a new sequence.

#### Scenario: An unavailable or malformed window arrives
- **WHEN** the backend returns rejected, insufficient, failed or unavailable data, or result validation fails
- **THEN** the panel displays a concise failure reason without fabricated power values
- **AND** it does not display a result-evidence expander
- **AND** a previous window is not relabelled as the current result
- **AND** waveform playback remains independent.
