## Why

Fixed seconds-per-screen changes the apparent EEG paper speed whenever a
window is resized and does not express the conventional clinical control such
as 30 mm/s. The live display needs a physical-layout speed setting separate
from signal sampling, acquisition time, raw storage, and scientific analysis.

## What Changes

- Replace fixed display-duration selection with standard paper speeds in mm/s.
- Derive current visible seconds from canvas width and WPF resolution-independent
  layout units.
- Persist the selected paper speed as a local workstation display preference.

## Compatibility Impact

This affects only waveform presentation. Sample counters, raw V/float64
recording, filtering, Python analysis, and scientific Runs are unchanged.

## Non-Goals

- This does not claim that OS-reported display layout measurements guarantee
  ruler-exact centimeters across every monitor; a future per-monitor ruler
  calibration is needed for that stricter claim.
- This does not change vertical sensitivity in uV/mm.
