# analysis/algorithm-definitions Specification

## MODIFIED Requirements

### Requirement: Persist immutable published definitions

The system SHALL retain existing published Definition identities and immutable
versions for historical reads and official Runtime provenance. Public clients
SHALL NOT create, clone, delete, edit, validate, publish or preview user-defined
algorithms. The platform MAY install official Definitions internally.

#### Scenario: Publish a correction

- **WHEN** an official Definition requires a corrected graph or metadata
- **THEN** the platform installs a new immutable SemVer version internally
- **AND THEN** the prior published version remains unchanged

#### Scenario: Read a historical user definition

- **WHEN** a client reads an existing user Definition and its versions
- **THEN** the stored graph, digest and publication state are returned unchanged
- **AND THEN** no retired graph executor is invoked

#### Scenario: Attempt new user authoring

- **WHEN** a client posts a Definition create, version, publish, validation or preview request
- **THEN** the API returns `410 USER_ALGORITHM_AUTHORING_RETIRED`
- **AND THEN** no Definition, preview Run or artifact is created
