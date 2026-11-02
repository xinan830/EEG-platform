# Centralize montage reference metadata parsing

## Why

Recording hardware metadata still uses a dictionary boundary for manifest
compatibility, while the scanner and store previously duplicated its string
keys and validation rules. A typed internal adapter makes that boundary easier
to audit without changing the manifest schema or recording behavior.

## Scope

- Centralize montage ID and snapshot parsing in one internal adapter.
- Preserve legacy behavior: blank IDs and blank snapshots do not create IDs,
  while a present blank snapshot remains invalid during strict acquisition
  validation.
- Add focused regression tests for missing, blank, valid, and conflicting data.

## Non-goals

- No manifest JSON schema change.
- No change to montage compatibility, deletion locks, snapshot immutability, or
  scientific signal selection.
- No change to the public dictionary-shaped registration APIs.
