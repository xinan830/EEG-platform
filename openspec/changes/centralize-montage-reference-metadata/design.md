# Design

`MontageReferenceMetadata` is an internal typed view over the existing
`IReadOnlyDictionary<string, string>` boundary. It treats blank montage IDs as
absent without trimming nonblank IDs, preserves raw snapshot text, and records whether the
snapshot key was present. ID collection ignores blank snapshot text as before;
strict acquisition validation checks key presence and therefore still rejects a
present blank snapshot.

The scanner and store use the same adapter. No caller-visible metadata field,
manifest serialization, or reference index format changes.

Keeping the parser on the scanner was rejected because acquisition validation
should not depend on scanning implementation details. This adapter owns only
interpretation, not persistence or lifecycle policy. No migration is needed;
rollback restores the two previous parsing locations without rewriting files.

## Verification

Run the focused montage lifecycle/reference tests, a Release desktop build,
strict OpenSpec validation, `git diff --check`, and the repository file-size
policy check.
