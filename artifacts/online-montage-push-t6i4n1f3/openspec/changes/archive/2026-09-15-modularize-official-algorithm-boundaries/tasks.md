## 1. Registry and compatibility boundaries

- [x] 1.1 Add official algorithm contract models and a single five-algorithm registry.
- [x] 1.2 Extract frozen official calculation entry points into per-algorithm modules and retain legacy facades.
- [x] 1.3 Split shadow/reference validation exports by official algorithm while retaining the legacy shadow module import path.

## 2. Catalog and frontend integration

- [x] 2.1 Add the read-only official catalog API and fail-closed structured error behavior.
- [x] 2.2 Derive Definition capability execution kinds from the registry.
- [x] 2.3 Render the official algorithm picker group from the catalog independently of user definitions.

## 3. Validation and documentation

- [x] 3.1 Add registry, catalog, capability, and compatibility-boundary regression coverage.
- [x] 3.2 Run backend full suite, frontend Vitest/type check/production build, diff check, and OpenSpec strict validation.
- [x] 3.3 Archive this change after every task and gate passes.
