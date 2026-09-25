# analysis/scientific-backend-boundaries Specification

## ADDED Requirements

### Requirement: Maintain one authoritative home for active scientific behavior

The system SHALL assign every active scientific capability one authoritative
implementation. Compatibility modules MAY delegate to that implementation but
SHALL NOT carry a second active formula, scientific contract, or quality rule.

#### Scenario: Migrate Welch PSD

- **WHEN** the Welch PSD primitive moves from a legacy location to the
  scientific boundary
- **THEN** active callers use the new authority or a thin delegating adapter
- **AND THEN** no second active Welch implementation is introduced

#### Scenario: Detect duplicate active science

- **WHEN** architecture checks find two active production implementations for
  the same declared primitive or official algorithm
- **THEN** the check fails unless one implementation is explicitly marked as a
  non-executable validation reference or a delegating compatibility adapter

### Requirement: Preserve strict scientific dependency boundaries

Scientific primitives SHALL not depend on transport, persistence, service, or
display concerns. Official algorithms SHALL depend only on Runtime contracts,
models, and scientific capabilities. Services SHALL orchestrate use cases and
SHALL NOT implement scientific formulas.

#### Scenario: Service requests spectral analysis

- **WHEN** a service performs a recording-backed spectral use case
- **THEN** it resolves input, cache, persistence, and response orchestration by
  calling a scientific primitive
- **AND THEN** it does not implement Welch, FFT, filtering, band integration,
  or peak detection itself

#### Scenario: Primitive is tested without infrastructure

- **WHEN** a scientific primitive is tested with an in-memory V/float64 signal
- **THEN** the test requires no FastAPI application, SQLite database, artifact
  store, or recording-service instance

### Requirement: Keep the Runtime as the only generic execution engine

Every active official algorithm SHALL be registered and invoked through
`algorithm_runtime`. Static/dynamic scheduling, parameter validation, module
dispatch, and Runtime-level evidence behavior SHALL not be duplicated inside
algorithm modules or services.

#### Scenario: Run an official algorithm

- **WHEN** a Run selects an installed official algorithm
- **THEN** the Runtime validates and dispatches the module selected by its
  exact algorithm ID and scientific version
- **AND THEN** the generic Run service contains no algorithm-ID-specific branch

### Requirement: Preserve science during structural migration

Structural migration SHALL not change an algorithm's formulas, preprocessing,
quality thresholds, units, canonical sample coordinates, output semantics, or
scientific version. Any such change SHALL be isolated into a separate approved
scientific-contract change.

#### Scenario: Compare a migrated primitive

- **WHEN** a scientific primitive is moved to its new authoritative boundary
- **THEN** a frozen baseline comparison and independent-reference tests
  demonstrate equivalence using the declared exact-field rules and per-output
  `rtol`/`atol` values before the old authority is retired

### Requirement: Keep historical user-defined analysis evidence readable

The platform MAY retire creation and execution of user-defined algorithms, but
it SHALL preserve historical user-defined definitions, Runs, result summaries,
provenance, and artifacts as read-only data. Historical reads SHALL not load or
execute a retired user-defined algorithm engine.

#### Scenario: Read a retired user algorithm Run

- **WHEN** a user opens an existing Run produced by a retired user-defined
  algorithm
- **THEN** the backend returns stored result and provenance evidence
- **AND THEN** the response identifies the execution path as unavailable for
  new Runs without attempting to recreate it

### Requirement: Migrate repositories without changing data meaning

SQLite migrations and repositories SHALL have one persistence ownership
boundary. Moving a repository from `services` to `persistence` SHALL preserve
table schema, foreign-key behavior, transaction semantics, serialization, and
historical data readability.

#### Scenario: Move the Run repository

- **WHEN** Run persistence code moves to the persistence boundary
- **THEN** existing analysis Runs, artifacts, cancellation states, queue
  recovery, and idempotency records remain readable and behaviorally identical

### Requirement: Enforce migration boundaries continuously

The backend SHALL maintain architecture tests that reject forbidden production
imports, direct active use of retired compatibility implementations, and new
scientific formulas in service modules.

#### Scenario: Add a forbidden service import

- **WHEN** a service imports an internal scientific implementation to add a new
  calculation instead of using the declared scientific interface
- **THEN** the architecture test fails before the change is accepted

### Requirement: Assemble concrete official modules outside the generic Runtime

The system SHALL keep `algorithm_runtime` independent of concrete official
algorithm packages. A single production composition root SHALL import concrete
modules under `algorithms`, register them through the Runtime contract, and
serve as the only built-in registration boundary.

#### Scenario: Start the backend with built-in algorithms

- **WHEN** the application composition root starts
- **THEN** it registers each enabled official algorithm with
  `algorithm_runtime`
- **AND THEN** importing `algorithm_runtime` alone does not import any
  concrete module under `algorithms`

### Requirement: Keep service calls within an explicit scientific boundary

Services SHALL use the Runtime for official algorithm Runs and MAY call
`scientific` directly only for a primitive-only use case. Services SHALL NOT
reimplement a primitive, create a hidden algorithm-specific dispatch branch, or
bypass the declared scientific contract.

#### Scenario: Request a reusable PSD primitive

- **WHEN** a service needs PSD for a non-algorithm-specific display or quality
  use case
- **THEN** it calls the typed `scientific` primitive contract directly
- **AND THEN** it does not create an official algorithm Run or duplicate PSD
  mathematics

### Requirement: Preserve retired user-defined catalog identity

The system SHALL retain historical user-defined algorithm identities in the
catalog and SHALL expose their retirement state with
`status=retired`, `executable=false`, `creatable=false`, and `editable=false`.
The retired entry SHALL remain readable for historical Runs and SHALL reject
new creation or execution with a stable unavailable response.

#### Scenario: List a retired user-defined algorithm

- **WHEN** the catalog is requested after user-defined execution retirement
- **THEN** the historical entry is returned with all four retirement flags
- **AND THEN** the entry is not offered as executable or editable

#### Scenario: Read history after retirement

- **WHEN** a historical Run references the retired entry
- **THEN** its stored definition, result, and provenance remain readable
- **AND THEN** no retired executor is imported or invoked

### Requirement: Track scientific authority in a machine-readable manifest

The system SHALL maintain one `ScientificAuthority` manifest covering each
named primitive and official algorithm. An authoritative implementation SHALL
declare `authority=true`; a compatibility adapter SHALL declare
`authority=false` and `delegate_to`; and a validation-only implementation SHALL
declare `executable=false` and `reference_only=true`. Runtime registration and
duplicate-authority checks SHALL consume this manifest.

#### Scenario: Detect a second active implementation

- **WHEN** two entries claim authority for the same scientific capability
- **THEN** architecture validation fails with both identities and their
  canonical paths

#### Scenario: Keep a reference implementation non-executable

- **WHEN** a validation reference is present for an independent comparison
- **THEN** it is available to validation tests
- **AND THEN** it cannot be registered with Runtime or called by an active
  service

### Requirement: Validate numerical equivalence against frozen baselines

Every structural migration of a scientific capability SHALL compare against a
versioned frozen baseline fixture. Discrete, identifier, unit, coordinate, and
provenance fields SHALL use exact equality. Floating-point scalars and arrays
SHALL use explicitly named per-output `rtol` and `atol` values recorded with
the test. An unspecified or inherited tolerance SHALL not satisfy migration
acceptance.

#### Scenario: Migrate a floating-point primitive

- **WHEN** the migrated primitive is compared with its frozen baseline
- **THEN** the test reports the output field, shape, maximum absolute error,
  and configured `rtol`/`atol`
- **AND THEN** the migration fails if any field exceeds its declared rule

#### Scenario: Verify a sample-coordinate field

- **WHEN** a migrated result contains canonical sample coordinates
- **THEN** those coordinates are compared exactly
- **AND THEN** a numerical tolerance cannot conceal a coordinate shift
