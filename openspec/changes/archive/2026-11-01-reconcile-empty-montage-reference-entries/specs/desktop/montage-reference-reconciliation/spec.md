## Purpose

Keep persisted montage reference protections usable when a recording path was discovered before its manifest supplied montage identity metadata.

## ADDED Requirements

### Requirement: Empty discovered reference entries can be reconciled
The desktop SHALL verify montage references from the durable index and recording manifests before allowing edits or deletion. When an indexed recording path has an empty identity set and its readable manifest provides a valid non-empty montage identity, the desktop SHALL reconcile that identity into the index. When an indexed non-empty identity set differs from the manifest, verification SHALL fail and the desktop SHALL preserve the existing index and prohibit edits or deletion.

#### Scenario: Reconcile an empty discovered identity entry
- **WHEN** a recording path is indexed with no montage IDs and its manifest contains a montage ID consistent with its snapshot
- **THEN** reference discovery records the manifest identity and completes verification
- **AND** the referenced montage remains protected from deletion

#### Scenario: Preserve protection for a changed established identity
- **WHEN** a recording path has a non-empty indexed identity set that differs from its manifest
- **THEN** verification fails without replacing the indexed identity or unlocking the referenced montage

#### Scenario: Reject internally conflicting manifest identities
- **WHEN** a manifest montage ID conflicts with its snapshot ID
- **THEN** verification fails and does not reconcile the index
