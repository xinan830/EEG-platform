# Algorithm validation baseline

## Purpose

Define engineering validation views and independent checks without turning frontend display code into a second scientific implementation.

## Requirements

### Requirement: Display backend algorithm evidence read-only

Validation dialogs SHALL display algorithm versions, time ranges, reference, filters, sampling frequency, frequency grid, window configuration, quality counts, units, and backend-returned values without recomputing EEG metrics in the frontend.

#### Scenario: Inspect a spectral result

- **WHEN** the user opens algorithm validation for F3
- **THEN** the dialog shows the executed backend contract and channel values associated with that result

### Requirement: Support single-window PSD inspection

Static spectral validation SHALL allow a 4-second absolute window and display all 117 backend PSD points from 1.00 through 30.00 Hz at 0.25 Hz spacing.

#### Scenario: Inspect 10-14 seconds

- **WHEN** the user selects a static single window starting at `10.000 s`
- **THEN** the dialog shows the `10.000-14.000 s` range, 117 frequencies, and 117 linear PSD values in `uV^2/Hz`

### Requirement: Compare a spectrogram row to static PSD

The validation view SHALL request the static 4-second PSD corresponding to a selected spectrogram center and report maximum absolute error, maximum relative error, and pointwise pass/fail. Error summarization MAY occur in the frontend; source spectra SHALL come from the backend.

#### Scenario: Compare center 12 seconds

- **WHEN** center `12.000 s` is selected
- **THEN** spectrogram row `12.000 s` is compared with static PSD for `10.000-14.000 s` on the same channel and contract

### Requirement: Distinguish engineering and clinical validation

The system SHALL label current automated checks as engineering validation and SHALL NOT infer diagnostic or clinical validity from a passing result.

#### Scenario: Produce PASS

- **WHEN** two implementations agree within the declared floating-point tolerance
- **THEN** the report states engineering parity only and does not produce a clinical conclusion
