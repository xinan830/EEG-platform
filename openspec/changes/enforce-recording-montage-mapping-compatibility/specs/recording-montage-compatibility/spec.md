# Recording Montage Compatibility

## ADDED Requirements

### Requirement: Apply only verified selections

The review dropdown MUST show only compatible montages. The session MUST reject
profile objects outside its verified catalog, even when an ID matches.

#### Scenario: Unverified object reuses a compatible ID

- WHEN a different profile object uses the ID of a compatible montage
- THEN the session MUST reject its application and preserve the selection

#### Scenario: No compatible montage

- WHEN no montage is compatible
- THEN the selector MUST be disabled with an explanation
- AND review MUST retain the raw-channel view

#### Scenario: Manifest source disagrees with the historical snapshot

- WHEN a required manifest source has a different native index or channel kind
  from the historical snapshot
- THEN the catalog MUST reject the montage with a diagnostic reason

### Requirement: Verify channel-configuration provenance and immutable mapping

The review catalog MUST show a candidate montage only when its bound channel
configuration identity equals the recording's historical configuration identity.
It MUST then compare the candidate's bound channel snapshot with the recording's
historical channel snapshot for every source signal used by the montage.

#### Scenario: Swapped native inputs

- GIVEN a recording snapshot maps CH0 to F3 and CH1 to F7
- AND a candidate montage snapshot maps CH0 to F7 and CH1 to F3
- WHEN the review catalog evaluates the candidate
- THEN it MUST classify the candidate as incompatible
- AND it MUST report that the physical channel mapping differs

#### Scenario: Same mapping from a different configuration

- GIVEN two channel configurations have the same device and required native mappings
- AND their configuration IDs differ
- WHEN the candidate is evaluated
- THEN it MUST be hidden from the review selector

### Requirement: Verify all source dependencies

The catalog MUST verify positive, negative, average-reference, and specified-pair
source labels, including their units and unique manifest mapping.

#### Scenario: Mean reference

- GIVEN a montage output uses an average group F3, F7, and Cz
- WHEN any member is absent or mapped to a different native input
- THEN the montage MUST be incompatible

#### Scenario: Missing historical snapshot

- GIVEN a recording has no immutable channel snapshot
- WHEN a candidate montage is evaluated
- THEN the catalog MUST report that compatibility cannot be verified
- AND it MUST NOT classify the candidate as compatible

### Requirement: Preserve historical projection

The compatibility change MUST NOT rewrite raw samples or change the projector's
historical source-channel lookup.

#### Scenario: Compatible remount uses historical source data

- GIVEN a montage is compatible with the recording mapping
- WHEN the review projector renders the montage
- THEN it MUST use the recording's historical source channels
- AND it MUST leave the raw sample payload unchanged
