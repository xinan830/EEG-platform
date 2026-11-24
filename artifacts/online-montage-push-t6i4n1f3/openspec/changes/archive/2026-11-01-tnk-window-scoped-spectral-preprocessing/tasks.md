## 1. Contract and source boundary

- [x] 1.1 Add the TNK bounded spectral-source and on-demand delta specifications.
- [x] 1.2 Extend the typed spectral request and recording service plumbing with
  an explicit bounded TNK scope while preserving the generic default.
- [x] 1.3 Implement finite-context sample reading and zero-phase preprocessing
  with V/float64, channel-order, gap, cancellation, and provenance checks.

## 2. TNK integration and identity

- [x] 2.1 Route TNK dynamic child and group spectral requests through the bounded
  scope; leave static and non-TNK consumers on their declared paths.
- [x] 2.2 Version the result/filter provenance and prevent continuous cache reuse
  for bounded requests.
- [x] 2.3 Add focused contract checks for first-window read bounds, edge clamping,
  and bounded version identity.

## 3. Acceptance

- [ ] 3.1 Perform the declared bounded-versus-continuous numerical comparison on
  representative windows and record the result without claiming equivalence.
- [ ] 3.2 Run the required focused validation and `git diff --check`; report any
  unavailable environment or user-recording validation separately.
