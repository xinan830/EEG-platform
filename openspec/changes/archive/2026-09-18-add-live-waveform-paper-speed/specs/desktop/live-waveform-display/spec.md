## ADDED Requirements

### Requirement: Live waveform paper speed is a physical-layout presentation setting

The desktop SHALL offer supported live waveform paper speeds of 5, 10, 15, 30,
and 60 mm/s. It SHALL derive the visible horizontal time duration from the
rendered canvas width and selected paper speed, rather than from a fixed
seconds-per-screen selection. The speed SHALL be a local display preference and
SHALL NOT alter acquisition sampling rate, sample counters, raw EEG values,
raw persistence, display filtering, or scientific analysis.

#### Scenario: User selects 30 mm/s

- **WHEN** the nominal rendered width is 30 mm and paper speed is 30 mm/s
- **THEN** the x-axis SHALL represent one second across that width
- **AND** changing the width or speed SHALL recompute visible seconds without
  inventing or advancing received samples

#### Scenario: User changes paper speed during acquisition

- **WHEN** the user changes paper speed while the waveform is rendering
- **THEN** the renderer SHALL rebuild only presentation paging and time-axis
  geometry from already received samples
- **AND** raw persistence and the acquisition read loop SHALL continue unchanged

#### Scenario: A monitor has uncertain physical calibration

- **WHEN** the workstation display cannot guarantee ruler-exact physical length
- **THEN** the desktop SHALL retain resolution-independent nominal layout speed
- **AND** it SHALL NOT represent the setting as calibrated clinical paper output
