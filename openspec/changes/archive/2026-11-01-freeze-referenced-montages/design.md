# Design

## Ownership

`MontageRecordingReferences` owns historical-reference discovery and the durable
ID lock index adjacent to the montage catalog. `MontageProfileStore` serializes
mutations and freeze operations, checks locks at the persistence boundary, and
returns read-only presentation facts. The workspace uses those facts for editor
permissions, while store checks also protect stale drafts and direct callers.

The follow-up `release-unused-montage-references` separates historical edit locks
from current deletion locks. The durable index retains discovered roots and
recording paths; permanent removal of the last verified reference permits preset
deletion (including archived presets), but never overwrite or reuse of its ID.

The raw-writer factory freezes the serialized montage selected for recording
before publishing a manifest. A saved profile with the same ID but different
content, a deleted user preset, or inconsistent metadata/snapshot IDs is rejected
before capture starts. Tests inject an isolated catalog.
Reviewing an externally opened recording also registers its historical reference.

Copy creates a new ID with revision 1, preserving the bound channel snapshot,
formulas, colors and order. Archive changes only availability metadata; the
historical snapshot and scientific fingerprint stay unchanged. Archived presets
remain inspectable/copyable and are excluded from future acquisition choices.

## Review boundary

`RecordingMontageCatalog` and `RecordingReviewSession` retain their verified
projection capabilities. Review defaults to the acquisition snapshot (or explicit
raw fallback when no valid snapshot exists). The selector offers only profiles
accepted by the existing same-channel-ID and scientific compatibility checks.
Choosing another compatible profile changes the display projection, not the
stored acquisition snapshot, raw data or analysis source. Missing history is not
guessed from current presets. Locking an editor does not remove rereferencing.
Equivalent projections share frame samples, but a cache hit refreshes the selected
preset's display name so the chart does not identify the previous preset.

## Non-goals

No physical mapping, units, timeline, filter, algorithm or raw-payload changes.
No unrelated workspace refactor; existing oversized workspace files receive
updated scoped size assessments rather than mechanical splitting.
