# Proposal

## Why

Disabled EEG channels still appear in recording review, impedance detection, and some live analysis views because each feature applies its own selection rules. A single display projection is needed so the saved acquisition or import-time configuration controls visibility without changing raw samples.

## What Changes

- Define one desktop rule that projects enabled EEG channels in configured display order and preserves each channel's source index.
- Apply the rule to recording review, impedance readings, live analysis channel choices, and related channel displays.
- For EDF/BDF imports, bind the current channel configuration only when every EEG file channel has one unambiguous matching configured EEG channel; otherwise retain the source-file channel presentation.
- Carry the export channel identity manifest back into import, verified against the exact exported EDF/BDF bytes, so shortened or aliased EDF labels retain their original electrode identities.
- Use one validated EEG projection for live waveform, live analysis, impedance, and review while retaining raw source column order.
- Declare prepared imported EEG arrays in volts, matching the numeric sample access contract, while preserving original EDF/BDF units separately as source metadata.
- Persist the successful import-time configuration and file-column mapping so later profile edits cannot change historical recordings.
- Preserve original source labels, sample matrices, and file column order for storage and backend analysis.

## Capabilities

### New Capabilities
- `desktop/enabled-channel-projection`: consistent enabled-channel display behavior across acquisition and review surfaces.

### Modified Capabilities
- `data/recordings`: persist an optional import-time channel configuration binding while preserving source-order raw data.

## Impact

Desktop channel domain, recording review projection, impedance and live analysis presentation; backend recording import model, migration, import API/service and metadata response; desktop recording access DTO; focused desktop and backend tests. No scientific formula, unit, sample ordering, or backend algorithm input semantics change.
