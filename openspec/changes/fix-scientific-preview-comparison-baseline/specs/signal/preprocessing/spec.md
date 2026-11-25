## ADDED Requirements

### Requirement: Scientific preview compares a shared scientific filtering baseline

Scientific before/after previews SHALL use one immutable source, effective analysis montage, sample range and scientific filter specification for both sides. The baseline SHALL disable bad-channel treatment, artifact QC and artifact removal. The processed side SHALL apply the requested policies in the established scientific order. Viewer-filtered samples SHALL NOT be used as the baseline.

#### Scenario: All enhanced policies are disabled

- **WHEN** a paired preview requests no bad-channel treatment, artifact QC or artifact removal
- **THEN** both sides SHALL contain identical scientific values and missing masks at every original sample, reuse one preparation result, and declare that only the common scientific filtering is active

#### Scenario: A bad channel affects average reference

- **WHEN** the processed side excludes or interpolates a source electrode used by average reference
- **THEN** treatment SHALL precede re-referencing as required by the scientific pipeline, the baseline SHALL retain the untreated reference inputs, and dependent-output changes SHALL be attributed to that treatment rather than a different filter

#### Scenario: Source identity changes during preparation

- **WHEN** a source revision or selected montage identity differs between the two preparations
- **THEN** the service SHALL reject the comparison rather than publish a mixed-source pair

### Requirement: Paired scientific previews declare comparable coordinates and evidence

Paired previews SHALL declare a comparison schema version, source and montage identity, common effective filter evidence, and each side's ordered channels and preprocessing evidence. Coordinates SHALL preserve absolute recording seconds and half-open sample ranges. Internal samples SHALL remain float64 V; API waveforms SHALL identify microvolts. Excluded outputs SHALL remain explicit and SHALL NOT be matched by column position.

#### Scenario: Processed output excludes a channel

- **WHEN** a channel exists in the scientific baseline but is removed by processed bad-channel policy
- **THEN** the baseline channel SHALL remain available, the processed side SHALL identify its exclusion, and another output SHALL NOT be substituted into its row

#### Scenario: Artifact rejection or ICA makes an output unavailable

- **WHEN** processed samples are unavailable in an interval
- **THEN** they SHALL remain null gaps in the API with the existing detector or reconstruction-dependency evidence, while unaffected baseline samples remain unchanged

#### Scenario: Response cannot prove a complete pair

- **WHEN** a response has an obsolete schema, inconsistent filters, mismatched identities or invalid channel/time/value arrays
- **THEN** the comparison consumer SHALL reject it explicitly rather than use causal display samples or a prior processed trace as a replacement

### Requirement: Paired preview compression preserves both scientific traces

Scientific comparison compression SHALL use a shared sample-index grid per matching output, preserve each side's temporal extrema and every missing-data break, and return each selected side's actual sample value without interpolation. Point budgets and mandatory-boundary exceptions SHALL be explicit. Compression SHALL NOT change scientific prepared data, analysis inputs or cache identities.

#### Scenario: Only one side contains a short gap or a narrow peak

- **WHEN** a missing interval or temporal extreme occurs in either side
- **THEN** both traces SHALL use the shared selected times, each SHALL retain its own values and missing mask, and neither trace SHALL bridge the missing interval

#### Scenario: Mandatory gap or total response budget is exceeded

- **WHEN** mandatory gaps or the aggregate channel count would exceed 16000 points per trace or 1000000 time/value scalars across both sides (including nulls)
- **THEN** the service SHALL stop envelope collection before serialization and return HTTP 422 with code PREPROCESSING_PREVIEW_LIMIT_EXCEEDED, request_id and a message requesting a shorter viewport
- **AND** the service SHALL NOT erase gaps, interpolate samples or zero-fill to fit the budget

#### Scenario: Rendering density changes

- **WHEN** only the preview point budget changes
- **THEN** compression SHALL use the same prepared scientific sources, preserve the common filter evidence and declare the new display-only budget
