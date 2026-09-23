# Change: Close channel and montage configuration chain

## Why

Channel profiles can currently be saved without complete semantic validation,
and applying a profile mutates live configuration state before persistence has
succeeded. Editing an applied profile also leaves the old applied snapshot in
use while the catalogue incorrectly labels the edited content as current.
Montage profiles retain a source snapshot, but the catalogue does not disclose
whether that snapshot still matches its source channel profile.

## What Changes

- Validate channel profile labels, display selections, input identities, and
  display orders before either saving or applying a profile.
- Persist a validated applied profile before publishing it as the current
  in-memory configuration.
- Distinguish an exactly applied channel snapshot from a catalogue profile with
  unapplied edits by comparing semantic fingerprints.
- Classify every montage profile as synchronized, source-modified,
  source-removed, or device-incompatible without rewriting its saved snapshot.
- Lock signal-bearing fields and deletion of a channel profile after any
  montage references it, while allowing non-semantic name/description edits.
- Let operators create a new editable channel-profile version instead of
  rewriting or deleting the referenced profile.

## Scope

This change only closes the configuration-management chain. It does not add a
montage selector to acquisition, execute rereferencing, or change raw EEG data.
