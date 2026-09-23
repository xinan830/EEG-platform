## 1. Project workspace

- [x] 1.1 Add persistent project identity, metadata, directory, search, and CRUD.
- [x] 1.2 Replace static project and recording examples with bound empty/data states.
- [x] 1.3 Preserve project files when a project is removed from the workstation index.

## 2. Acquisition ownership

- [x] 2.1 Require a project context in every acquisition request.
- [x] 2.2 Store sessions below the selected project's recordings directory.
- [x] 2.3 Persist project identity and snapshot separately from hardware configuration.
- [x] 2.4 Start the device stream only from the project-scoped preparation page.
- [x] 2.5 Separate live preview from explicit raw recording and return to the project after completion.

## 3. Verification

- [x] 3.1 Add project persistence and acquisition ownership tests.
- [x] 3.2 Run desktop Release tests, strict OpenSpec validation, and diff checks.
