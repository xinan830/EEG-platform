# Bind live channel settings to selected montage outputs

## Why

The live settings popup previously showed physical device-input labels and a
static EEG count even after the user selected a display montage. It also exposed
reference, order, label, scale, preprocessing, algorithm, and validation
controls that either duplicate montage ownership or have no live implementation.

## What changes

- The popup shows the selected montage name and its derived output count.
- Its selectable list contains only derived montage output names; selection is
  display-only visibility for the current live session.
- The montage owns rereference formulas and output order.
- Remove unsupported or duplicate controls and the preprocessing, algorithm,
  and validation tabs; retain the amplifier tab.

## Scope

Raw device inputs, persisted channel configuration, montage formulas, and
scientific analysis reference are unchanged.
