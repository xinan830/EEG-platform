## Why

Operators configuring display montages need to inspect which physical electrode each derived lead points toward before saving the configuration.

## What Changes

- Add a floating, read-only direction map to montage configuration.
- Draw each montage relationship as an arrow from its positive electrode to each negative reference electrode, including REF, selected channels, average reference members, and specified-pair members.
- Keep electrode coordinates as display metadata; do not plot values or alter signal/reference calculations.
