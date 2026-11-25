## ADDED Requirements

### Requirement: Publish immutable prepared scientific samples
Shared prepared scientific samples SHALL be read-only when published to consumers. Their V/float64 values, channel order, absolute sample bounds and gap semantics SHALL remain unchanged by another consumer. Callers needing mutations MUST copy the samples.

#### Scenario: Two algorithms share prepared data
- **WHEN** two consumers obtain the same cached prepared source
- **THEN** assignment through the published array is rejected and subsequent readers observe the original samples

### Requirement: Invalidate in-flight scientific cache publication
Invalidation of a recording SHALL detach pending work from future cache lookups. Work started before invalidation SHALL NOT repopulate prepared-source or reviewed-model caches after it finishes. Existing consumers MAY retain their owned results until release.

#### Scenario: Clear while continuous preparation is running
- **WHEN** a recording cache is invalidated while an earlier preparation is in flight
- **THEN** a later request does not join that invalidated work and the earlier result cannot overwrite a newer cached result

### Requirement: Resolve effective offline preparation consistently
Offline requests SHALL resolve filter parameters, quality policies, reference identity and supported scope before scientific execution. Execution and provenance SHALL use the same effective configuration. Continuous and finite-context preparation SHALL retain their distinct declared boundary semantics.

#### Scenario: Equivalent callers prepare the same source
- **WHEN** algorithm and inspection callers request the same source, channels and effective preprocessing specification
- **THEN** they use the same scientific preparation semantics and compatible cache identity without caller-selected bypass execution

#### Scenario: Unsupported finite-context artifact removal
- **WHEN** a finite-context request includes ICA, SSP or EOG removal
- **THEN** execution is rejected explicitly instead of silently omitting that operation
