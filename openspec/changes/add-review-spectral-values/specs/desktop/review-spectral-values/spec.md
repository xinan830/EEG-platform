## Purpose

Enable numerical inspection of the existing PSD and RBP results during recording playback.

## ADDED Requirements

### Requirement: Display source algorithm values
The review panel SHALL display the selected band's RBP absolute power in uV^2 and relative power, and all PSD frequency points and densities in that band, per ordered montage output, using the existing algorithm results and their declared units.

#### Scenario: Current valid window
- **WHEN** both source windows are available for the current recording and compatible analysis configuration
- **THEN** the panel displays their matching sample range, channel names, quality and numeric values without replacing PSD points with a summary.

### Requirement: Show derived band RMS amplitude
The panel SHALL display RMS amplitude in uV derived by the backend as the square root of the corresponding nonnegative RBP absolute power in uV^2.

#### Scenario: Unavailable power
- **WHEN** a channel's band power is unavailable or invalid
- **THEN** its amplitude SHALL be unavailable with a reason rather than a fabricated zero.

### Requirement: Reject stale or mismatched source results
The panel SHALL retain separate source identity, window, channel order, processing settings and quality and SHALL NOT present mismatched windows as a matched result.

#### Scenario: Seek or montage change
- **WHEN** playback position or analysis montage changes while requests are outstanding
- **THEN** obsolete responses SHALL NOT overwrite the current selection and the panel SHALL show loading or unavailable status until current results are ready.
