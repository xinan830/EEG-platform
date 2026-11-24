## ADDED Requirements

### Requirement: Export a reproducible result package

The system SHALL export an AnalysisRun as a confined package containing a
manifest, result summary, verified artifacts, declared units, quality state,
and implementation/configuration/source identities. It SHALL exclude source
EEG samples, original filename, participant identity, and clinical conclusion.

#### Scenario: Export a completed PSD Run
- **WHEN** a completed Run with a verified NPZ artifact is exported
- **THEN** the package includes its artifact SHA-256 and enough manifest data to
  identify the run configuration and implementation without recomputing it

### Requirement: Keep rejected output explicit

The system SHALL export a gate-failed or unavailable result as `null` with its
structured quality/error state and SHALL NOT synthesize zero values.

#### Scenario: Export a quality-gated Run
- **WHEN** a Run ends as `gate_failed`
- **THEN** its exported manifest and result identify the gate reason and contain
  no fabricated numerical output
