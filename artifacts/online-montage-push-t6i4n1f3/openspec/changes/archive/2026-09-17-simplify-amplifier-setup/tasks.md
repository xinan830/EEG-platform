## 1. Device Capability Flow

- [x] 1.1 Return device-reported reference and bipolar ranges during ANT discovery.
- [x] 1.2 Permit SDK-path-only configuration for connection testing while requiring selected ranges for stream opening.

## 2. Operator Workflow

- [x] 2.1 Replace manual range/DLL workflow with amplifier settings, file selection, and test connection.
- [x] 2.2 Populate device, rate, and range selectors only from actual discovery output.
- [x] 2.3 Keep raw recording directory in an advanced local storage section.

## 3. Verification

- [x] 3.1 Add regression tests for capability preservation and no-range discovery configuration.
- [x] 3.2 Run Release build, tests, strict OpenSpec validation, and diff checks.
