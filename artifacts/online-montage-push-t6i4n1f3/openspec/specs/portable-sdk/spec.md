# portable-sdk Specification

## Purpose
TBD - created by archiving change package-licensed-ant-sdk. Update Purpose after archive.
## Requirements
### Requirement: Portable release carries the licensed x64 SDK
The portable release SHALL contain `sdk/eego-SDK.dll` copied from the configured
licensed x64 source path.

#### Scenario: New machine starts portable release
- **WHEN** no valid local SDK path is saved
- **THEN** WPF uses the package-local x64 SDK path

#### Scenario: Local SDK path remains valid
- **WHEN** a user has selected an existing SDK path
- **THEN** that path remains preferred over the bundled copy

#### Scenario: Missing SDK source
- **WHEN** the release script cannot find the licensed source DLL
- **THEN** packaging fails before producing a misleading incomplete release

