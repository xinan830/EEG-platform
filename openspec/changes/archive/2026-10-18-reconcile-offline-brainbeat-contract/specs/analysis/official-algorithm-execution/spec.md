## MODIFIED Requirements

### Requirement: Catalog state matches executable scope

The official catalog SHALL derive availability and runnable state from each
installed validated runtime module rather than an obsolete algorithm-name
allowlist. IAPF, Theta/Beta, FAA and offline Brainbeat SHALL be available and
runnable when their corresponding validated modules are installed. This
offline capability SHALL NOT enable a stateful realtime Brainbeat path.

#### Scenario: Read catalog after the cutover

- **WHEN** a client requests the official catalog with the validated IAPF, Theta/Beta, FAA and offline Brainbeat modules installed
- **THEN** those entries are available and runnable for their declared modes
- **AND** each reports its module-owned scientific and implementation identity
- **AND** offline Brainbeat availability does not claim realtime IAPF-lock or EMA support
