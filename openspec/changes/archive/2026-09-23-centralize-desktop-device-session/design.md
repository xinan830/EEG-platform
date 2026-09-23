## Context

See [proposal.md](proposal.md). The acquisition view model previously owned
discovery collections, selected-device state, and the side effect that loaded
channel mapping. The configured runtime already owns the serialized adapter and
native-stream lifecycle, so a new owner must not duplicate that responsibility.

## Goals / Non-Goals

**Goals:**

- Publish one immutable device snapshot and one discovered-device collection.
- Preserve the common-driver boundary and use only returned descriptors.
- Allow channel-profile editing to resolve the same selected descriptor as
  acquisition setup.
- Keep waveform-display preferences separate so the acquisition view model
  does not grow further.

**Non-Goals:**

- Do not infer a model from a display name or add a model field without a
  common adapter contract.
- Do not split streaming from recording in this change.
- Do not persist projects, montages, or acquisition-session snapshots yet.

## Decisions

### Session manager wraps published facts, not native stream ownership

`DeviceSessionManager` uses the existing `ConfiguredAcquisitionRuntime` for
configuration/discovery and subscribes to its state transitions. The runtime
and coordinator remain the only owners of adapters, streams, raw writers, and
fault cleanup. This prevents a second lifecycle gate around an ANT stream.

Alternative: move the coordinator into the session manager. Rejected because
it would combine the generic stream lifecycle with WPF-facing discovery state
and substantially expand this migration.

### Capability compatibility uses a deterministic fingerprint

The fingerprint hashes driver ID plus returned channel input index, role, and
unit sorted by input index and role. It intentionally excludes display name,
serial number, labels, project, montage, and selected sampling rate. This lets
identical hardware layouts reuse a profile while refusing a layout that only
looks similar by name.

Alternative: bind profiles to serial number. Rejected because identical lab
devices should reuse a configuration unless a later user explicitly scopes one
to a serial.

### Setup and display preferences remain separate consumers

The acquisition view model keeps selected sampling rate, ranges, raw-recording
directory, and recording commands. `LiveDisplayPreferencesViewModel` owns
display filtering and paper-speed preference persistence. The channel-profile
editor reads `DeviceSessionManager` directly; navigation does not pass a
device descriptor.

## Risks / Trade-offs

- [Idle unplug has no vendor notification] → The manager reports a runtime
  fault or a later discovery result; it does not falsely promise instantaneous
  disconnect detection without an SDK-supported probe.
- [Driver reconfiguration recreates an adapter] → Stream-opening options are
  applied only while idle, and the selected discovery descriptor remains a
  session fact until opening either succeeds or faults.
- [Existing large acquisition view model] → Display preferences are extracted
  now; device setup remains the next stable extraction before adding more
  acquisition controls.

## Migration Plan

1. Compose one runtime and one session manager in `App`.
2. Route current discovery, selection, and invalidation through that manager.
3. Route channel-profile draft resolution through the same manager.
4. On rollback, remove the session manager composition; no local recording,
   channel profile, raw EEG, or backend database migration is involved.

## Storage Ownership

The session manager has no persistent storage. It publishes current hardware
facts in memory. Existing local workstation settings retain only user
preferences; raw recording ownership remains with the acquisition coordinator.
