## Purpose

Provide one observable contract for reading and presenting completed algorithm results so that algorithm modules only define scientific output semantics while shared infrastructure handles window access consistently.

## ADDED Requirements

### Requirement: Use a shared completed-result window contract

Completed full-analysis window reads SHALL use one contract containing Run and artifact identity, recording identity, ordered window geometry, status, quality, failure details, axes, units, and provenance. An algorithm SHALL provide a typed descriptor for its scientific output arrays and time semantics; it SHALL NOT require a separate transport or cache path for the same window behavior.

#### Scenario: Read PSD and STFT through the same path

- **WHEN** a completed PSD or STFT Run is queried at an absolute cursor sample
- **THEN** the shared reader selects the matching stored window and returns the same status, range, failure, and provenance fields, with only the declared scientific arrays differing

#### Scenario: Reject an identity mismatch

- **WHEN** the artifact Run, recording, algorithm version, or request identity does not match the completed Run
- **THEN** the reader returns a structured cache/integrity failure and does not expose arrays as a valid result

### Requirement: Preserve explicit scientific semantics

The shared contract SHALL preserve backend-provided sample ranges, seconds derived from the validated sampling rate, channel order, SI units, display units, quality state, and provenance. It SHALL represent unavailable or rejected scientific values as null with a structured reason and SHALL never synthesize zeros or inferred EEG values.

#### Scenario: Legacy metadata is incomplete

- **WHEN** an older artifact lacks fields that are required by the current descriptor but its stored geometry and versioned compatibility rule are sufficient
- **THEN** the reader may provide only the documented compatibility metadata, marks the compatibility path in diagnostics, and retains the stored scientific arrays unchanged

#### Scenario: Legacy metadata cannot be reconstructed

- **WHEN** required time, unit, channel, or provenance semantics cannot be established
- **THEN** the reader returns an explicit unavailable/integrity failure and the UI displays the reason without a numeric fallback

### Requirement: Support bounded deterministic window reads

The shared reader SHALL validate the structured result once at load time, index windows by sample geometry, and perform cursor reads using the validated index without rescanning numerical payloads. Window selection SHALL use absolute recording sample coordinates and retain the backend window state (`Partial`, `Complete`, `Rejected`, or `Unavailable`).

#### Scenario: Seek within a completed full result

- **WHEN** the user drags the cursor across a completed full Run
- **THEN** each cursor request is a bounded read of the matching stored window and does not start algorithm execution or filter warmup
