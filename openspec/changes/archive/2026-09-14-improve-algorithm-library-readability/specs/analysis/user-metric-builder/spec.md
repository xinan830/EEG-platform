## ADDED Requirements

### Requirement: Saved user metrics are readable in the algorithm library

The normal algorithm-library view SHALL render a saved metric made with the
curated builder as its named inputs, permitted binary operation, named output,
and declared output unit. It SHALL not require a user to inspect node IDs,
graph adapters, or JSON to understand that metric.

#### Scenario: Review a saved Theta/Beta metric

- **WHEN** a user selects a saved metric composed from Theta power divided by
  Beta power
- **THEN** the library shows `输入 A: Theta 功率`, `÷`, `输入 B: Beta 功率`,
  and the named ratio output
- **AND THEN** it states that actual values are produced by backend execution,
  not calculated in the browser.

### Requirement: Only safe private definitions may be removed

The algorithm library SHALL offer a confirmed Delete control only for private
definitions. The backend SHALL reject deletion of platform-official definitions
and definitions referenced by a saved AnalysisRun or BatchRun.

#### Scenario: Remove an unused private metric

- **WHEN** a user confirms deletion of a private definition with no run or
  batch references
- **THEN** the backend deletes its definition record and versions
- **AND THEN** the library removes that entry.

#### Scenario: Attempt to remove protected definition

- **WHEN** a user or client requests deletion of an official or referenced
  definition
- **THEN** the backend returns a structured conflict error
- **AND THEN** the definition, versions, and provenance records remain intact.

### Requirement: Browser clients cannot create platform-owned definitions

The public definition-creation API SHALL only accept a local-user owner. The
backend MAY create platform-official definitions through internal installation
paths.

#### Scenario: Browser claims official ownership

- **WHEN** a browser sends `owner: platform-official` while creating a
  definition
- **THEN** the API returns `DEFINITION_OWNER_FORBIDDEN`
- **AND THEN** no definition is created.
