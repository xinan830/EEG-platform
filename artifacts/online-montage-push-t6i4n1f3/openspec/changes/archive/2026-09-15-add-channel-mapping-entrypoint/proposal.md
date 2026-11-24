# Add channel mapping entrypoint

## Why

Official Theta/Beta correctly requires a persisted Fz/Pz/Oz semantic mapping,
but the normal workbench did not expose the existing mapping editor. Users
could see valid raw channels yet had no path to confirm an O2-to-Oz role
assignment.

## What Changes

- Persist a mapping at import only when Fz, Pz, and Oz all match source labels
  exactly after case/whitespace normalization.
- Add a normal-workbench channel-mapping entrypoint and save flow.
- Offer O2 as an Oz suggestion only in the manual confirmation dialog.
- Keep waveform viewing and channel selection independent from semantic
  mapping.

## Non-Goals

- Do not infer Oz from O2, O1, source position, or any alias.
- Do not alter EEG math, references, analysis APIs, or existing mappings.
