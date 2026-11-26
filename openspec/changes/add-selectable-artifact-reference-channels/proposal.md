# Proposal

## Why
Users cannot select recorded EOG/ECG references in Review. Dedicated auxiliary signals must reach artifact scoring without becoming EEG outputs.

## What Changes
- Add typed source-voltage discovery, persisted EEG/EOG/ECG/unconfirmed identities, EOG multi-select and ECG single-select.
- Carry explicit raw-source selections through review, preview and algorithm preparation.
- Score ICA with separately aligned dedicated auxiliary arrays; EEG proxies retain EEG fitting/montage membership with automatic/manual numerical equivalence.
- Invalidate confirmed models when selections or saved identities change; preserve identities in EDF/BDF export/import and report actual references.

## Capabilities
### New Capabilities
None.
### Modified Capabilities
- `signal/preprocessing`: explicit artifact references, alignment, role isolation and identity.

## Impact
WPF/EDF readers, preprocessing API/science/services and desktop Review/HTTP contracts/tests. No dependency or database migration. Existing empty selections retain auto/proxy behavior. No realtime/display-filter changes, hardware setup changes or automatic clinical classification. Deploy backend and desktop together; rollback together and refit ICA. V/float64 and absolute sample times remain unchanged.
