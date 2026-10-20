## MODIFIED Requirements

### Requirement: Backend exposes an authoritative official-algorithm catalog

The official catalog SHALL derive runnable official metadata from the installed
runtime module manifest, including Definition identity, scientific version,
implementation identity, output unit, modes and availability. A non-runnable
official capability without a runtime module MAY use one explicit static
descriptor. The active scientific version SHALL be explicitly declared for
each official algorithm ID; catalog clients SHALL NOT select retired versions.

#### Scenario: Read the catalog

- **WHEN** a client requests `/api/algorithms`
- **THEN** it can distinguish official from user algorithms and render each
  selected algorithm's inputs without inspecting internal DAG or adapter data
- **AND** each official algorithm ID exposes only its explicitly active version

#### Scenario: All installed official algorithms are listed

- **WHEN** a client requests `GET /api/algorithms`
- **THEN** the response lists every installed platform official algorithm
- **AND** executable items report their runtime-owned runnable state without
  an installed Definition prerequisite
- **AND** an explicit descriptor for a capability without an installed executor is not represented as runnable

#### Scenario: Installed evidence is unavailable

- **WHEN** the runtime registry cannot be resolved
- **THEN** the response returns user algorithms plus `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`
- **AND** no incomplete official item is represented as runnable

#### Scenario: Read catalog after RBP and FAA enablement

- **WHEN** a client requests `/api/algorithms` with validated RBP, FAA and offline Brainbeat modules installed
- **THEN** those entries are available and runnable with readable Chinese labels and declared input fields
- **AND** offline Brainbeat does not advertise a stateful realtime execution mode

#### Scenario: Read a runnable official catalog item

- **WHEN** a client reads `/api/algorithms`
- **THEN** each runnable official item exposes the same identity and version that will be persisted and executed for a Run
- **AND** the client does not infer identity from the algorithm name or a duplicated catalog mapping

#### Scenario: Retired version is not selectable

- **WHEN** a client requests the catalog after an official algorithm advances to a new current version
- **THEN** the catalog exposes the declared current version only
- **AND** it does not advertise or select a retired calculator version for new Runs

### Requirement: Official algorithm availability is not inferred by clients

The frontend SHALL render official algorithm label, purpose, status, and disabled state from the catalog response. It SHALL NOT derive them from an algorithm name, graph, quality rules, or Definition owner field.

#### Scenario: Catalog failure does not hide user algorithms

- **WHEN** the official catalog request fails while the user Definition request succeeds
- **THEN** the user algorithm list remains available
- **AND THEN** the official section presents a scoped catalog failure message.

### Requirement: Official algorithm refactoring preserves frozen contracts

The system SHALL keep each official algorithm's scientific calculation in its
own module and SHALL preserve that current module's numerical, unit,
channel-order, quality, null, and timing semantics during management-layer
changes. It SHALL NOT add compatibility adapters for retired scientific
versions as part of algorithm management.

#### Scenario: Missing required channel role

- **WHEN** an official algorithm requires Fz/Pz/Oz or F3/F4 roles and the caller does not provide an explicit mapping
- **THEN** it is unavailable with a structured reason
- **AND THEN** the system does not infer Oz from O2 or from channel order.

#### Scenario: Consolidate management metadata

- **WHEN** common catalog or registration metadata is consolidated
- **THEN** each algorithm's formula and algorithm-specific scientific policy remain owned by its module
- **AND** numerical, unit, channel-order, quality, unavailable-value, and timing behavior remains unchanged

### Requirement: Keep non-runnable official algorithms unavailable

The catalog SHALL keep capabilities without validated executable modules
non-runnable. It SHALL NOT disable installed validated FAA or offline
Brainbeat modules merely because an earlier rollout left them disabled.
IAPF and Theta/Beta v2 SHALL expose their own raw channel parameter schemas
and SHALL NOT declare required global roles.

#### Scenario: Read Theta/Beta catalog metadata

- **WHEN** a client reads the Theta/Beta catalog entry
- **THEN** it describes one selected raw channel and contains no Fz/Pz/Oz mapping prerequisite

#### Scenario: Read offline Brainbeat catalog metadata

- **WHEN** the validated offline Brainbeat module is installed
- **THEN** its entry is runnable with explicit distinct Fz and Pz source inputs
- **AND** its supported modes remain static and dynamic offline execution

#### Scenario: Reject incomplete management registration

- **WHEN** an official module is missing required catalog metadata or its declared desktop integration
- **THEN** the release completeness check fails with the missing registration points identified
- **AND** scientific execution remains unavailable through an incomplete catalog entry
