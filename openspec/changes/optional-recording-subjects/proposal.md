# Proposal

## Why
Import currently forces an unexplained subject identifier while the recording list never displays it. Acquisition, import and record editing need the same optional subject code/name behavior.

## What Changes
- Make Session subject association nullable without creating placeholder people.
- Accept optional subject code and name during import and acquisition, and allow record editing to set, change or clear them.
- Display subject identity in project recording rows; preserve existing associations.
- Keep subject data separate from immutable signal files and scientific provenance.

## Capabilities
### New Capabilities
- `desktop/recording-subjects`: Optional subject metadata across acquisition, import, editing and project lists.
### Modified Capabilities
- `data/research-projects`: Optional Session subject and user-entered subject name.

## Impact
- Backend project models, migration, subject resolution, import endpoint and Session editing; desktop project client, dialogs, recording list and acquisition preparation/local metadata.
- Existing API callers with subject IDs remain supported; null subject IDs are now valid. Database migration preserves identities and foreign keys.
- Non-goals: subject management application, person matching by name, algorithm changes, rewriting EDF/BDF headers or changing export provenance. File header identity prefill is deferred to avoid implicit disclosure or ambiguous identity binding.
- Scientific time, units, channel order, montage, filters, algorithms and WebSocket contracts remain unchanged. Names are explicitly user-supplied local metadata; never log them.
