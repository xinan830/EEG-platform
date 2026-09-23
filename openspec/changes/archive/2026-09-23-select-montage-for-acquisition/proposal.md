## Why

The channel-configuration "apply" action creates a second global source of
truth even though an acquisition already selects a montage that owns an
immutable channel-configuration snapshot. This allows the applied channel
profile and the selected montage source to disagree.

## What Changes

- Remove the user-facing apply action and automatic restoration of a global
  channel configuration from device discovery.
- Treat saved, valid channel profiles as selectable sources for montage
  profiles; a montage keeps its channel snapshot.
- Add acquisition preparation selection of a validated montage and sampling
  rate.
- Load the selected montage's channel snapshot only for the acquisition
  session and persist both channel and montage snapshots in stream metadata.
- Render the selected montage from retained raw batches without mutating raw
  EEG samples.

## Impact

This changes desktop configuration and acquisition selection behavior only.
Raw EEG persistence, sample-counter time semantics, units, and backend science
remain unchanged.

## Migration Strategy

Existing channel and montage files remain readable. Existing active-channel
files are ignored for new discovery sessions; the selected montage is the
authoritative acquisition input. Existing profiles continue to be usable when
their snapshot and device capability are valid.

## Non-Goals

- Add enable/disable state to channel or montage profiles.
- Rewrite historical recordings.
- Change scientific analysis reference semantics.
