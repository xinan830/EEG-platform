## ADDED Requirements

### Requirement: Validate desktop inputs using backend catalog bounds
The desktop SHALL validate provided numeric config fields against catalog bounds
before submitting a Run or clearing the previous preview. Limits SHALL come from
the backend catalog rather than algorithm-specific desktop constants.

#### Scenario: Unsupported search band
- **WHEN** a requested frequency exceeds the catalog maximum
- **THEN** no Run is submitted and a Chinese parameter label and limit are shown.

### Requirement: Preserve structured HTTP failures
The desktop SHALL report HTTP failures without assuming detail is a string.

#### Scenario: Validation list
- **WHEN** the server returns an array of validation errors
- **THEN** the client exposes an AlgorithmApiException with the HTTP status and
  available details, without throwing a JSON kind conversion exception.
