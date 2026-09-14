# analysis/algorithm-form-builder Specification

## Purpose
Provide local research users a safe, traceable way to author and inspect
algorithm definitions without duplicating scientific validation or EEG math in
the browser.
## Requirements
### Requirement: Author a canonical definition draft

The system SHALL provide a form and advanced JSON representation of one shared
definition draft, including identity, SemVer, inputs, outputs, graph nodes,
parameters, units, quality rules, and references. A valid edit in either view
SHALL update the other view without discarding unspecified graph fields.

#### Scenario: Edit an input through the form
- **WHEN** a user changes a draft input or its declared unit in the form
- **THEN** the advanced JSON representation SHALL show the same canonical draft

#### Scenario: Invalid advanced JSON
- **WHEN** a user enters syntactically invalid JSON
- **THEN** the system SHALL preserve the last valid draft, show a local parse error, and SHALL NOT send a save or preview request

### Requirement: Use backend validation as the scientific authority

The system SHALL submit canonical drafts to the backend for validation before
creating a version, publishing it, or running a preview. The browser SHALL NOT
derive unit compatibility, graph validity, channel suitability, or EEG output.

#### Scenario: Backend rejects a graph
- **WHEN** backend validation returns a structured graph or unit error
- **THEN** the workbench SHALL display its stable code and SHALL NOT create or publish a version

### Requirement: Preserve immutable version lifecycle

The system SHALL let a user create a draft definition, create a version, clone
a definition, compare two versions, and publish only a backend-valid version.
Published versions SHALL remain read-only; changing one SHALL require a new
SemVer version or a cloned definition.

#### Scenario: Clone an official definition
- **WHEN** a user clones an official definition
- **THEN** the system SHALL create a distinct user-owned definition and retain the original version unchanged

### Requirement: Keep composite definitions honest

The workbench SHALL expose the execution category and quality contract of an
official composite definition. It SHALL NOT present a composite-only official
definition as editable and executable through the generic graph runtime.

#### Scenario: Inspect IAPF
- **WHEN** a user selects an IAPF definition marked composite-only
- **THEN** the workbench SHALL display its immutable contract and SHALL disable generic graph execution for that version

### Requirement: Run an isolated preview with provenance

The system SHALL create a persisted preview AnalysisRun from a recording
identity, absolute requested range, canonical draft, and explicitly typed
scalar simulation inputs. Its output SHALL be labeled preview, retain units
and quality state, and SHALL NOT overwrite a formal run or its artifacts.

#### Scenario: Complete a scalar preview
- **WHEN** a validated draft and typed scalar simulation inputs are submitted
- **THEN** the system SHALL return an AnalysisRun with `is_preview=true`, a
  preview-only result summary, and the requested/actual absolute range

#### Scenario: Reject unavailable preview data
- **WHEN** a preview input is absent, invalid, or has an incompatible unit
- **THEN** the system SHALL return a structured error and SHALL NOT fabricate a zero-valued output
