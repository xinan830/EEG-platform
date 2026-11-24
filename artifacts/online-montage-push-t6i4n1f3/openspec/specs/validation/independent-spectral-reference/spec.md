# validation/independent-spectral-reference Specification

## Purpose
TBD - created by archiving change add-independent-spectral-reference-validation. Update Purpose after archive.
## Requirements
### Requirement: Independently validate a frozen spectral PSD

The system SHALL provide a read-only operation that compares a selected
recording range and requested channel order from `offline-spectral-v3` with an
independent NumPy/SciPy implementation of the locked 1--30 Hz, 4 s Hann Welch,
50% overlap contract.  The reference implementation SHALL NOT call production
preprocessing, Welch PSD, or band-power functions.

#### Scenario: Persist pointwise PSD parity evidence
- **WHEN** a valid range containing at least one complete four-second segment
  and available channels is requested
- **THEN** the system SHALL persist a `ValidationRun` containing source
  identity, configuration digest, tolerances, maximum errors, pass rate,
  environment, and ordered pointwise PSD evidence in uV^2/Hz
- **AND THEN** the result SHALL be labelled engineering validation only, not
  clinical validation.

#### Scenario: Preserve requested channel order
- **WHEN** the request specifies channels in a non-file order
- **THEN** evidence and comparison flattening SHALL use that requested order.

#### Scenario: Do not fabricate a failed comparison
- **WHEN** the production or reference path has no finite PSD because its
  quality gate fails
- **THEN** the operation SHALL return an unavailable structured result and
  SHALL NOT persist zero-filled PSD evidence.

### Requirement: Expose derived evidence through existing reports

The validation report and a result export that selects the validation SHALL
include the derived PSD evidence, frequency coordinates, units, and reference
implementation identity.  They SHALL exclude raw EEG samples, source
filenames, and participant identity fields.

#### Scenario: Review an exported validation report
- **WHEN** an analyst exports a result with an independent spectral validation
- **THEN** `validation-report.json` SHALL contain the comparison evidence and
  engineering-only interpretation required to reproduce the numerical check.

