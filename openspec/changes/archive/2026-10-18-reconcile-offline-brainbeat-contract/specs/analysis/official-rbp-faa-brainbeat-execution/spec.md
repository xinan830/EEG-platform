## MODIFIED Requirements

### Requirement: Preserve the stateful BrainBeat boundary

The installed offline Brainbeat module SHALL remain available for static and
dynamic AnalysisRuns as defined in `analysis/official-brainbeat`. Offline
windows SHALL estimate their own IAPF independently and SHALL NOT carry
IAPF-lock or EMA state between windows. Offline availability SHALL NOT imply
that stateful realtime Brainbeat is enabled or scientifically equivalent.
A stateful realtime path SHALL require its own session, IAPF-lock, EMA,
quality, provenance and validation contract before enablement.

#### Scenario: Read BrainBeat in the catalog

- **WHEN** a client loads the official algorithm catalog with the validated offline Brainbeat module installed
- **THEN** Brainbeat is available and runnable for its declared static and dynamic offline modes
- **AND** its scientific version and implementation identity come from its module manifest
- **AND** this entry does not declare a stateful realtime mode

#### Scenario: Offline windows do not inherit realtime state

- **WHEN** two offline Brainbeat windows execute in a different request order
- **THEN** each uses its own window IAPF and source spectra
- **AND** neither consumes prior-window EMA or IAPF-lock state
