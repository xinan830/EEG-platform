# Design

## Context
ANT display strings occur in device adapters, setup pages, channel/montage presets and TNK tab labels. Montage deduplication currently classifies by a localized name prefix. Existing snapshots are immutable scientific evidence.

## Goals / Non-Goals
Use one POP display brand, identity-based built-in profile presentation and name-independent deduplication. Do not rename native types/IDs/SDK symbols, alter vendor evidence or algorithms, migrate recordings, rewrite user names or introduce ANT/POP text aliases. Existing unrelated worktree edits are out of scope.

## Decisions
Own device display brand in the device contracts layer. Own default channel identity/display in the existing channel-template owner and known montage identities in the preset factory. Add JsonIgnore presentation properties for saved profiles and bind UI/status/draft presentation to those properties. Keep Name and Description serialized verbatim. Semantic dedup uses built-in source/identity plus device/configuration/formulas rather than names.

Reject global replacement and legacy text-prefix adapters because they mix identity and branding. Keep native vendor evidence as recorded; change only application-owned display strings. Tests cover old names with stable identities, custom names, unchanged JSON/fingerprints, device metadata, SDK failure and montage catalogue behavior. No new dependencies.

## Migration / Rollback
No migration or second format. New system profiles store POP names; old snapshots retain original names and render through the same identity-based presentation. Roll back the presentation code together.

## Risks / Trade-offs
Missed bindings can expose a saved name, so scan callers and verify acquisition/review/configuration UI. Keep physical-line caps and existing high-risk viewmodel scope; avoid unrelated splitting. No hardware scientific validation is implied.
