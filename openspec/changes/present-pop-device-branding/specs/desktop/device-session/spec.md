## ADDED Requirements

### Requirement: Product display branding is independent of hardware identity
The desktop SHALL present the built-in device product as POP in discovery, acquisition setup, connection and impedance messages and product-owned analysis labels. Native driver/device IDs, SDK ABI/library names, serial/model facts and vendor provenance SHALL remain unchanged.

#### Scenario: A device is discovered or opened
- **WHEN** the built-in hardware driver discovers a device or opens an EEG/impedance stream
- **THEN** product display names use POP while native device identity, serial and capability fields retain their values

#### Scenario: A connection error is shown
- **WHEN** application-owned driver validation or readiness fails
- **THEN** its explanatory product text uses POP and its stable error code remains unchanged
