# analysis/legacy-compatibility Specification

## REMOVED Requirements

### Requirement: Retired user algorithms are not a product surface

The product SHALL expose only official Runtime algorithms for new analysis
Runs. Legacy user-algorithm Definition, preview, analysis, result, and artifact
read routes SHALL NOT be registered.

#### Scenario: Legacy route is requested

- **WHEN** a client requests `/api/analyses/{id}` or
  `/api/algorithm-definitions/{id}`
- **THEN** the API responds as an unknown route

## ADDED Requirements

### Requirement: Raw recordings are preserved during cleanup

The cleanup migration SHALL remove only retired user-algorithm data and its
derived artifacts. It SHALL preserve the recordings table and raw recording
files.

#### Scenario: Cleanup runs on an existing database

- **WHEN** migration 010 is applied
- **THEN** no `definition_metric`, `definition_preview`, or local-user
  Definition rows remain
- **AND** official Definition rows remain
- **AND** raw recording rows and files remain
