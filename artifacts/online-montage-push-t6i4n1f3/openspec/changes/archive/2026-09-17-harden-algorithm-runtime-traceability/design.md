## Decisions

### Resolve once, persist once, execute once

Official Run resolution selects a concrete runtime module and its immutable
published Definition identity. The queue persists those resolved values, and
the executor receives the resolved scientific version rather than selecting a
latest module again. This makes Run provenance, cache identity and numerical
execution refer to the same contract.

### Module-owned execution snapshot

Each runtime algorithm returns a serializable execution snapshot containing
its window, preprocessing and quality-rule contract. Generic Run services store
the snapshot verbatim. This prevents new algorithm science from becoming a
conditional in `RunService`.

### One runnable official manifest

Runnable official modules own their display, version, lifecycle and output
metadata in their runtime manifest. The official catalog derives runnable
entries from those manifests. BrainBeat remains a deliberately separate
non-runnable descriptor because it has no stateless Run module.

### Evidence envelopes are validated at the boundary

Common provenance evidence remains available for every algorithm. Modules may
add algorithm-specific evidence, but serialization validates the common shape
and named extension envelope before persistence. Missing optional evidence is
represented as absent, never fabricated as zero.

## Risks

- Existing queued rows retain their stored historical identity and remain
  readable; only newly enqueued Runs gain the fully resolved relation.
- Official Definition installation is idempotent and uses the existing SQLite
  Definition store; it must not overwrite published versions.
- The catalog remains intentionally closed and manually registered. This is a
  security boundary for the local platform, not a plugin-discovery system.
