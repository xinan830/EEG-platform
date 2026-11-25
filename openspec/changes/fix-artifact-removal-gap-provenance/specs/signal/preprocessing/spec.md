## MODIFIED Requirements

### Requirement: Distinguish transform unavailability from detected artifacts
Applied offline artifact removal SHALL report absolute half-open intervals of unavailable output, identifying missing input channels and additionally unavailable output channels separately from authoritative artifact detections. Preview and algorithm evidence SHALL share this application validity. No missing input SHALL be replaced with zeros or untreated raw samples represented as cleaned.

#### Scenario: A local gap prevents multichannel ICA reconstruction
- **WHEN** one ICA input channel is unavailable for an interval and selected components are removed
- **THEN** unavailable reconstructed outputs retain gaps with missing-input provenance, while the detector report continues to identify only its original affected channels

#### Scenario: No ICA components are excluded
- **WHEN** the reviewed exclusion set is empty
- **THEN** signal values and per-channel missing masks remain unchanged without additional reconstruction gaps

#### Scenario: Window evidence and failed spectral gates
- **WHEN** a preview or algorithm window overlaps a reconstruction gap
- **THEN** its evidence reports the clipped absolute interval and affected channels, and failed quality gates preserve this explanation

#### Scenario: Cache semantics change without refitting
- **WHEN** application validity evidence is introduced
- **THEN** active removal result identities change while the unchanged reviewed ICA fit identity remains stable
