## ADDED Requirements

### Requirement: Show the actual analysis range on PSD and RBP Review visualizations
The data Review PSD spectrum and topomap, and the RBP history charts and topomap, SHALL show the current visualization's actual analysis start and end times in seconds at the lower-left. The displayed range SHALL come from the corresponding backend result response. If that response does not provide a valid range, the visualization SHALL show an unavailable state rather than infer a range from the playback cursor or display history.

#### Scenario: Display a PSD spectrum range
- **WHEN** a PSD dynamic-window response with an actual start and end is rendered as a spectrum
- **THEN** the PSD spectrum shows that returned range in seconds at its lower-left

#### Scenario: Display a PSD topomap range
- **WHEN** a PSD topomap response with an actual start and end is rendered
- **THEN** the PSD topomap shows the topomap response's returned range in seconds at its lower-left

#### Scenario: Display RBP ranges
- **WHEN** an RBP dynamic-window response is rendered as history charts or a topomap
- **THEN** each RBP history chart and the topomap view show the corresponding current result range in seconds at the lower-left

#### Scenario: Backend range is missing
- **WHEN** a PSD or RBP visualization has no valid backend-returned analysis range
- **THEN** it shows that the range is unavailable
- **AND** it does not derive a substitute from the cursor or visible history
