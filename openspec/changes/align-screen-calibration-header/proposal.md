# Align screen calibration header

## Why
The screen calibration settings page still uses a stacked text return link and oversized title after the other settings pages adopted the shared icon navigation header.

## What Changes
Reuse the existing back-navigation button style and align the title, description and page spacing with settings pages.

## Capabilities
### New Capabilities
- `desktop/settings-presentation`: consistent screen calibration settings header.

## Impact
Only WPF presentation and a focused header regression test change. Calibration inputs, persistence, millimetre calibration, navigation handlers, time, signal units, channel order, API messages and scientific provenance stay unchanged. No migration is needed; reverting the XAML restores the old presentation.
