# Tasks

- [x] 1. Confirm the current backend RBP static/dynamic result contract.
- [x] 2. Parse current five-band shares (and historical four-band shares) and window states without client-side calculation.
- [x] 3. Add RBP-owned SciChart views and connect the shared dynamic cursor.
- [x] 4. Reuse the shared quality and provenance surfaces for RBP.
- [x] 5. Run WPF build, automated regression tests, and OpenSpec strict validation.
- [ ] 6. Restart WPF and visually verify static/dynamic RBP with a local recording.

Validation: backend RBP tests passed (including the five-band contract and
official static/dynamic run coverage), the existing WPF RBP test assembly
passed 11 tests with `--no-build`, OpenSpec strict validation passed, and
`git diff --check` produced no whitespace errors. A normal WPF build remains
blocked while the running desktop process holds `BrainPlatform.Desktop.exe`.
