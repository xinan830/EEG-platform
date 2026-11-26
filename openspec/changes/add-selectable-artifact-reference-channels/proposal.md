# Proposal

## Why
Users cannot select recorded EOG/ECG references in Review. Dedicated auxiliary signals must reach artifact scoring without becoming EEG outputs.

## What Changes
- Add typed source-voltage channel discovery and EOG multi-select / ECG single-select.
- Carry explicit raw-source selections through review, preview and algorithm preparation.
- Score ICA with separately aligned auxiliary arrays, preserve EEG-only fitting and output order.
- Invalidate confirmed models when auxiliary selections change; report actual references.

## Capabilities
### New Capabilities
None.
### Modified Capabilities
- `signal/preprocessing`: explicit artifact references, alignment, role isolation and identity.

## Impact
WPF/EDF readers, preprocessing API/science/services and desktop Review/HTTP contracts/tests. No dependency or database migration. Existing empty selections retain auto/proxy behavior. No realtime/display-filter changes, hardware setup changes or automatic clinical classification. Deploy backend and desktop together; rollback together and refit ICA. V/float64 and absolute sample times remain unchanged.
