## ADDED Requirements

### Requirement: Version FAA continuous preprocessing scope
FAA v2 SHALL preprocess the selected raw channel pair over the entire recording
before slicing static or dynamic windows and then remove each paired Epoch's
mean. It SHALL record this scope, filter settings, numerical identity and quality
evidence without rewriting historical v1 results.

#### Scenario: Static and dynamic share the same range
- **WHEN** static FAA and a dynamic FAA window use identical channels, filters and bounds
- **THEN** both consume the same continuously preprocessed samples and return equal science

#### Scenario: Read a historical FAA Run
- **WHEN** a caller retrieves a saved v1 result
- **THEN** its original identity, evidence and artifact remain readable without v2 execution
