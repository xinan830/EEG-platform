## ADDED Requirements

### Requirement: Select RBP topomap power representation
The RBP topomap SHALL accept an explicit power representation, `relative` or `absolute`, independently from the selected frequency band. The response SHALL identify the selected representation and declare `ratio` for relative maps or `uV^2` for absolute maps. Both representations SHALL use the same RBP result window, recording identity, montage snapshot, channel order, spatial eligibility rules, and interpolation contract. Omitting the representation SHALL select `relative` for existing callers. The client SHALL request the selected representation from the backend and SHALL NOT derive absolute power from relative shares or diagnostic evidence.

#### Scenario: User selects absolute power
- **WHEN** the user selects absolute power and a current RBP result contains structured absolute-power arrays
- **THEN** the selected frequency band is rendered from the corresponding absolute-power array with unit `uV^2`
- **AND THEN** the result uses the same current window and montage snapshot as the relative map

#### Scenario: User selects relative power
- **WHEN** the user selects relative power or an existing API caller omits the representation
- **THEN** the selected frequency band is rendered from the existing ratio array with unit `ratio`

#### Scenario: Absolute output is absent from a stored result
- **WHEN** absolute power is requested for a result that does not declare its structured absolute-power array
- **THEN** the API returns a structured unavailable map with a stable reason
- **AND THEN** it does not read or reconstruct a value from calculation-trace evidence, another window, or another montage version

### Requirement: Limit topomap interpolation to the selected frequency band
The run and dynamic topomap APIs SHALL accept an optional `band_id` from the closed set Delta, Theta, Alpha, Beta, and Gamma. When omitted, the response SHALL contain all five bands. When supplied, the response SHALL contain and interpolate only that band. Band selection SHALL be independent from power representation and SHALL use the same current result window, montage snapshot, channel order, units, and quality rules. It SHALL NOT trigger RBP recomputation. An unsupported band identifier SHALL be rejected as an invalid request.

#### Scenario: User selects one band
- **WHEN** the Review user selects Alpha and the active power representation is absolute
- **THEN** the client requests only Alpha for the current window
- **AND THEN** the backend returns and interpolates one `alpha` band in `uV^2`

#### Scenario: User selects all bands or an existing caller omits band_id
- **WHEN** the frequency-band selection is All or an API caller omits `band_id`
- **THEN** the backend returns the existing five-band response

### Requirement: Emit request-correlated topomap timing diagnostics
For a successful topomap request, backend diagnostics SHALL report the request ID, result window ID, recording ID, power representation, requested band filter, backend service duration, per-band interpolation duration, and response serialization duration. Diagnostics SHALL NOT include raw samples or result value arrays. Timings SHALL remain operational metadata and SHALL NOT change scientific results, cache identity, or algorithm version.

#### Scenario: Diagnose a slow single-band topomap response
- **WHEN** one topomap request takes longer than expected
- **THEN** its request-correlated log entries distinguish backend band interpolation from response serialization
- **AND THEN** the response still contains only the requested band and the original result identity
