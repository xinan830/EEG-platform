## MODIFIED Requirements

### Requirement: Brainbeat source channels are explicit and distinct
The Run configuration SHALL require both a primary Fz channel and a distinct
secondary Pz channel. The backend SHALL resolve channel names against the
recording without inferring roles from channel order or substituting a missing
channel. Explicit Fz-AVG and Pz-AVG average-reference outputs SHALL be accepted
alongside Fz/Pz and Fz-REF/Pz-REF. Bipolar outputs SHALL NOT be treated as these
single-electrode roles.

#### Scenario: Missing or identical channel pair
- **WHEN** Pz is missing or the two configured channels are identical
- **THEN** configuration or input resolution fails with a structured validation error
- **AND** no scientific result is produced

#### Scenario: Average-reference Brainbeat execution
- **WHEN** a selected valid analysis montage supplies explicit Fz-AVG and Pz-AVG outputs
- **THEN** static and dynamic Runs accept the pair and calculate from those projected signals
- **AND** the immutable selected montage and source identities remain in the result provenance

#### Scenario: Reject a bipolar or swapped role
- **WHEN** the primary channel is Fz-Pz or Pz-AVG, or the secondary channel is Fz-AVG
- **THEN** configuration validation fails before scientific execution
