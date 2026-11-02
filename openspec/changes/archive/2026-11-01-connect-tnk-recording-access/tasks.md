## 1. Workspace ownership

- [x] 1.1 Make `DesktopWorkspaceViewModel` own the recording session manager.
- [x] 1.2 Inject the shared manager into Review and TNK and release leases correctly.

## 2. TNK boundary

- [x] 2.1 Validate TNK registration input through the shared access lease.
- [x] 2.2 Add focused tests for shared ownership, validation failure, and lease release.

## 3. Verification

- [x] 3.1 Run affected desktop tests/build and strict OpenSpec validation.
- [x] 3.2 Record the backend sample-transport boundary in the architecture documentation.
