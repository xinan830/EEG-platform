# Tasks

## 1. Source-bound contract
- [x] 1.1 Add geometry validator and binding tests for malformed metadata, label collisions and absent-geometry compatibility.
- [x] 1.2 Validate at import and preserve the snapshot across transport/export; test round-trip and document the optional schema.

## 2. Scientific consumers
- [x] 2.1 Thread shared geometry through raw and montage preprocessing/inspection; test public-MNE interpolation equivalence and unchanged healthy channels.
- [x] 2.2 Use shared geometry for ICLabel and ICA scalp maps; test eligibility gates and geometry-sensitive prepared/ICA cache identities.

## 3. Integration verification
- [x] 3.1 Run focused backend and desktop checks, strict OpenSpec validation, size-policy and diff checks; retain validation evidence.
- [x] 3.2 Verify dataset axis/unit provenance, create a separate geometry-bearing import artifact and rerun interpolation and ICA classification; report results and limitations without claiming clean-truth accuracy.
