## ADDED Requirements

### Requirement: Explicit immutable acquisition signal selection

The WPF analysis list MUST use the historical acquisition montage outputs.
Raw registration MUST remain available with distinct identity and cache keys.

#### Scenario: Two-output acquisition montage

- WHEN the historical montage defines F7-REF and F3-REF
- THEN acquisition-montage registration MUST return those outputs only
- AND numerical execution MUST read their historical physical input mappings

#### Scenario: Bipolar and average reference

- WHEN the selected output subtracts a channel or a saved reference group
- THEN the backend MUST project that formula in V/float64 before preprocessing
- AND preserve missing-sample NaN values
- AND include formula and source mapping in Run provenance

#### Scenario: Missing or contradictory snapshot

- WHEN required historical snapshots are absent or disagree with manifest sources
- THEN registration MUST reject the analysis view with a specific error
- AND MUST NOT guess a mapping or change raw files
