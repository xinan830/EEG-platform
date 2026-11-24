## MODIFIED Requirements

### Requirement: Preserve scientific output and distinguish lifecycle states

The service SHALL distinguish source preparation, queued work, window computation,
ready scientific output, insufficient data, rejected or unavailable science,
cancellation, and execution failure. Missing scientific values SHALL be null
with structured reasons. A ready window SHALL match the corresponding full
dynamic Run within the declared contract for that algorithm: algorithms that
retain continuous preprocessing SHALL retain the existing continuous contract;
TNK dynamic spectral algorithms SHALL use the separately versioned bounded TNK
preprocessing contract and SHALL report that contract in provenance.

#### Scenario: PSD window equivalence

- **WHEN** a PSD window and its matching full dynamic Run window use identical
  source bytes, configuration, identity, and continuous scientific contract
- **THEN** their sample bounds, frequency axes, values, units, quality/evidence,
  and preprocessing provenance match within the declared tolerance
- **AND** the continuous implementation does not locally filter only the
  requested short window

#### Scenario: TNK bounded spectral window

- **WHEN** a TNK dynamic spectral window is requested
- **THEN** it reads and filters only the target range plus the bounded context
  declared by the current TNK preprocessing version
- **AND** its result reports the bounded preprocessing identity and any declared
  numerical difference from the former continuous contract

#### Scenario: RBP window equivalence

- **WHEN** an opted-in RBP window and its full dynamic Run use identical source
  and scientific configuration under RBP's existing contract
- **THEN** the five named Delta/Theta/Alpha/Beta/Gamma ratios, actual sample
  range, quality and full window evidence match within `rtol=1e-12,
  atol=1e-12`
- **AND** both paths use RBP's declared preprocessing scope and preserve its
  existing failure contract without zero shares

#### Scenario: Scalar spectral window equivalence

- **WHEN** Band Ratio, Peak Frequency, IAPF or Theta/Beta executes an opted-in
  single window with identical source and scientific configuration to a full
  dynamic Run under its declared preprocessing contract
- **THEN** its value, actual range, quality, failure and evidence match the
  corresponding full Run point within the declared tolerance
- **AND** continuous consumers retain continuous preprocessing, while a TNK
  bounded consumer reports the bounded TNK identity and boundary policy
- **AND** a missing value is never emitted as zero; Theta/Beta preserves its
  historical `dimensionless` unit

#### Scenario: Per-algorithm opt-in stays disabled until final acceptance

- **WHEN** additional algorithms pass their independent automated scientific and
  module validation during the user-approved migration phase
- **THEN** they may declare their window capability behind independent default-off
  backend and WPF flags
- **AND** GUI playback, parameters, failure visibility and rollback remain
  explicit final acceptance work, not implied by successful automated tests

#### Scenario: Scientific rejection differs from execution failure

- **WHEN** the requested samples fail a scientific quality rule
- **THEN** the service publishes or returns the algorithm's rejected or
  unavailable state and structured quantitative evidence
- **AND** it does not report the quality outcome as a fabricated success or
  generic infrastructure failure
