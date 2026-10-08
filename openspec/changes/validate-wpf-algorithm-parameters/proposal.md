# Validate WPF algorithm parameters

## Why
The desktop ignores catalog numeric bounds and assumes HTTP error detail is a
string. Unsupported frequency inputs therefore reach the backend, and structured
validation responses can throw while the client is attempting to report errors.

## What Changes
- Validate serialized numeric parameters against backend catalog bounds before
  clearing previews or submitting a Run. Display Chinese labels and limits.
- Preserve stable HTTP errors for string, object, array and malformed bodies.
- Add desktop regression tests. Scientific computation and layout are unchanged.

## Impact
Only desktop request validation and error presentation change. Backend validation
remains authoritative; no new algorithm-specific frequency constants are added.
