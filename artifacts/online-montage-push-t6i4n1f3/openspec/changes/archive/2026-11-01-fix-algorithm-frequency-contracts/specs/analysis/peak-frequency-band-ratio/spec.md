## ADDED Requirements

### Requirement: Enforce the frozen spectral domain before execution
Peak frequency and band ratio SHALL accept only finite band boundaries within
1-30 Hz with strictly increasing edges. Their parameter schemas SHALL publish
the same limits. Static and dynamic out-of-domain requests SHALL be rejected
before loading signal data or queueing a Run, not silently cropped.

#### Scenario: Request a partly unsupported peak band
- **WHEN** a client requests a peak over 25-40 Hz
- **THEN** validation rejects the request and no 25-30 Hz partial peak is published.

#### Scenario: Request an unsupported ratio band
- **WHEN** a client requests a denominator over 30-50 Hz
- **THEN** validation rejects it before spectral loading.

#### Scenario: Receive an incomplete spectrum
- **WHEN** a valid configured peak band is not fully covered by the input frequency axis
- **THEN** the result is unavailable with a structured reason, never a clean partial search.
