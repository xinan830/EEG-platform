## 1. Preview-run contract

- [x] 1.1 Add typed backend request/response models for scalar definition previews with recording/range provenance.
- [x] 1.2 Execute preview drafts only through the closed backend definition engine and persist `is_preview=true` AnalysisRuns.
- [x] 1.3 Add a definition capability endpoint exposing allowed nodes and composite-only execution boundaries.
- [x] 1.4 Add backend API/service tests for preview provenance, invalid units, unavailable output, and immutable formal runs.

## 2. Workbench API and state

- [x] 2.1 Add typed frontend definition API client and shared definition/workbench models.
- [x] 2.2 Build a single-source draft composable that synchronizes form state and advanced JSON safely.
- [x] 2.3 Test draft serialization, invalid JSON retention, structured backend errors, and request race protection.

## 3. Research workbench UI

- [x] 3.1 Add a compact workbench entry point and definition list with official/user state and versions.
- [x] 3.2 Implement input, preprocessing, window, metric, quality, output, and JSON authoring sections.
- [x] 3.3 Implement validate, save version, clone, publish, compare, and preview controls with explicit state and error handling.
- [x] 3.4 Render preview provenance, units, quality, and `preview` status without interpreting it clinically.

## 4. Verification and archive

- [x] 4.1 Preserve legacy API behavior and run backend regression tests, frontend type checks, Vitest, and production build.
- [x] 4.2 Document the form-builder contract, preview boundary, and test evidence.
- [x] 4.3 Pass OpenSpec strict validation, archive the change, and update the roadmap.
