# Dynamic Spectral Results UI Validation Report

- Backend tests: `287 passed`.
- Frontend type check: `vue-tsc --noEmit` passed.
- Frontend tests: `37 files, 96 tests passed`.
- Structured preview verifies artifact SHA-256 before reading arrays.
- Preview rejects more than `1,000,000` numeric cells and converts non-finite
  numeric cells to JSON `null`.
- Frontend receives backend axes, units, state rows, and values; it performs no
  scientific transformation or downsampling.
- Rollback point: commit `61b2632` before this UI integration.
