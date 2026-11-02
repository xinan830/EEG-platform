# Freeze Referenced Montages

## Why

User montage profiles currently remain editable after acquisition. Although raw
recordings retain snapshots, the shared preset ID can refer to different content
and review exposes both historical and current versions. The approved product
policy freezes referenced profiles while retaining same-channel review projections.

## Scope

- Lock all fields of a montage once it is referenced by a recording.
- Permit viewing, copying to a new ID, and archiving; prohibit historical overwrite.
  Deletion requires no remaining discoverable references, as refined in
  `release-unused-montage-references`.
- Discover references in known project recordings and recoverable trash, retaining
  a durable edit-lock index so moving/removing a recording does not permit overwrite.
- Freeze the selected snapshot before a new recording manifest is published.
- Keep the acquisition snapshot as the review default; retain the existing
  same-channel-ID and scientific-mapping compatibility checks for switching.
- Retain the internal compatibility resolver and projector without changing EEG math.

## Migration and limits

Existing manifests and samples are never rewritten. Known project manifests are
scanned for IDs/snapshots; unreadable metadata blocks mutations rather than guessing.
Unknown external recordings cannot be discovered without opening/importing them.
New acquisitions register a durable historical lock and recording location under
the same gate as manifest publication. Failed creation without remaining files
does not permanently prohibit deletion; historical overwrite remains forbidden.
Locks coordinate this desktop process, not independent desktop processes editing
the same JSON files. The version-2 deletion index and migration limits are defined
in `release-unused-montage-references`.

## Verification

Test used/unused edits, deletion, durable locks after restart, legacy reference
discovery including trash, malformed metadata, stale snapshots, copy/new identity,
archive and acquisition choices, and compatible review selection without replacing
the historical snapshot. Run desktop
tests/build, relevant backend contract tests, OpenSpec and diff/size checks.
