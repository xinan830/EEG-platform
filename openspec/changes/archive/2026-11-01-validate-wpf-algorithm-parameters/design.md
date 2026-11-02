# Design

Validate the exact serialized config using AlgorithmParameter minimum/maximum.
Ignore absent/null fields because optional defaults are owned by the backend.
Reject nonfinite numeric values and out-of-bound values using label_zh and unit.
Perform this before ClearPreviews so invalid edits retain the last visible result.

HTTP error parsing must inspect JSON kinds before accessing strings. Structured
detail remains available as JSON text; unknown/malformed bodies keep a Chinese
HTTP-derived fallback. Do not translate arbitrary scientific error text by guesses.

Verification covers schema-driven limits, inclusive boundaries, missing optional
values, actual Run submission prevention, both notch modes and HTTP error shapes.
Live desktop mouse interaction is separate from automated contract tests.
