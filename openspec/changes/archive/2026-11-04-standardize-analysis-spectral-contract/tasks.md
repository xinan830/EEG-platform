## 1. Contract and inventory

- [x] 1.1 Inventory official spectral algorithms' filter ranges, notch defaults, output ranges, windows, Welch settings, bands, and provenance.
- [x] 1.2 Define and version the shared analysis preprocessing baseline as 1–50 Hz with default 50 Hz/Q=30 notch; preserve explicit algorithm overrides.
- [x] 1.3 Add backend tests proving analysis preprocessing does not inherit review display-filter state and notch changes identity/provenance.

## 2. Backend execution

- [x] 2.1 Remove automatic notch inference from requested output range and require an explicit effective setting.
- [x] 2.2 Route compatible PSD-derived algorithm consumers through shared 1–50 Hz preprocessing while retaining algorithm-owned windows, Welch parameters, target ranges, bands, and quality rules.
- [x] 2.3 Make 50 Hz notch default and explicit off available in eligible algorithm configurations; record filter range, notch/Q, phase, and contract identity.
- [x] 2.4 Version prepared-source/cache and result identity so old scientific results are not reused under the new contract.
- [x] 2.5 Return a structured unavailable/validation outcome when requested 1–50 Hz coverage is unsupported by Nyquist.

## 3. Desktop and validation

- [x] 3.1 Expose analysis notch default/off in eligible algorithm settings and serialize it independently from review display filters.
- [x] 3.2 Update independent spectral references, per-algorithm tests, and provenance/API contract tests for the new preprocessing behavior.
- [x] 3.3 Update spectral contract documentation and validate backend, desktop, OpenSpec, and diff checks.
