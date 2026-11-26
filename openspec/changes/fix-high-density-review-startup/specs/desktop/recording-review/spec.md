## ADDED Requirements

### Requirement: Review startup is responsive and single-flight
The desktop SHALL show the review workspace before awaiting its initial waveform load and SHALL admit at most one opening operation while recording metadata is loading. Loading state SHALL describe the active stage. Closing or failing initialization SHALL release the owned session without publishing stale results.

#### Scenario: Repeated clicks while a high-density recording opens
- **WHEN** the user clicks review again before the first open completes
- **THEN** the desktop does not start another recording load
- **AND** the initial window displays loading state while its first frame is prepared

#### Scenario: Repeated CAR dependencies
- **WHEN** a montage reuses the same original source across many reference formulas
- **THEN** resolving its channel bindings validates each distinct source once using indexed original bindings
- **AND** duplicate, missing, native-index, kind and unit mismatches remain errors

### Requirement: Imported montage identities are resolved before display
The desktop SHALL derive missing display fingerprints from the complete validated embedded channel configuration and montage formulas using its existing canonical identity rules. It SHALL reject malformed semantic snapshots before catalogue deduplication or display, preserve the source JSON, and log foreground review failures with recording/session and absolute target context. Sample times, V units, column order, reference formulas and scientific processing SHALL remain unchanged.

#### Scenario: Imported snapshot omits saved desktop fingerprints
- **WHEN** a valid imported primary or catalogue montage has complete channel bindings and formulas but omits its saved fingerprints
- **THEN** review resolves stable fingerprints from those bindings and formulas
- **AND** initial display, timeline navigation and cached redisplay succeed without reimport

#### Scenario: Imported snapshot lacks a required mapping field
- **WHEN** a snapshot is missing its embedded channel configuration or contains invalid formula fields
- **THEN** the montage is unavailable with a readable reason rather than causing a null reference or exposing disabled channels
