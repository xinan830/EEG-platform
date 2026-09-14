# Validation runs

## Purpose

Persist engineering-validation evidence and export it as a portable JSON report that identifies the compared inputs, implementations, tolerances, numerical differences, and environment.

## Requirements

### Requirement: Persist validation runs

The system SHALL store each validation identifier, kind, status, subject run or algorithm, dataset identity, configuration digest, tolerances, expected/actual summaries, maximum absolute and relative error, pointwise pass rate, environment, timestamps, and structured failure details.

#### Scenario: Record a passing pointwise comparison

- **WHEN** all compared values are within declared tolerances
- **THEN** a completed validation run records `passed=true`, the observed errors, point count, pass rate, and reproducibility context

#### Scenario: Record an execution failure

- **WHEN** validation cannot complete because an input or artifact is unavailable
- **THEN** the validation run records `failed`, a stable error code, and no fabricated comparison values

### Requirement: Export portable JSON validation reports

The system SHALL expose a JSON report whose content is sufficient to audit what was compared without requiring access to the platform database.

#### Scenario: Export a validation report

- **WHEN** a client retrieves `/api/validations/{id}/report`
- **THEN** the response includes report schema version, validation identity, engineering-only scope, input and configuration identities, tolerances, outcomes, environment, and timestamps

### Requirement: Keep engineering scope explicit

Validation records and reports SHALL identify themselves as engineering validation and SHALL NOT claim clinical validity.

#### Scenario: Validation passes

- **WHEN** a validation comparison passes numerically
- **THEN** the report states that PASS means agreement under the declared engineering tolerance only

### Requirement: Provide validation resources

The API SHALL provide create, list, retrieve, and report operations under `/api/validations` with structured errors.

#### Scenario: Retrieve an unknown validation

- **WHEN** the requested validation identifier does not exist
- **THEN** the API returns HTTP 404 with stable code `VALIDATION_NOT_FOUND`
