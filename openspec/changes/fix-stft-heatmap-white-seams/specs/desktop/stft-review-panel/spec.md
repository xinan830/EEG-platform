## ADDED Requirements

### Requirement: Appended STFT tiles do not create artificial seams
The Review STFT heatmap SHALL render contiguous appended time columns without white gaps introduced by independent tile boundaries. It SHALL preserve the returned power values and SHALL leave unvisited time gaps and unavailable matrix cells empty.

#### Scenario: Append a contiguous STFT block
- **WHEN** a refreshed result adds columns immediately after the displayed history
- **THEN** the rendered tiles overlap using an existing backend column to cover their shared raster edge
- **AND** the overlap does not change the time axis or power values

#### Scenario: Append after an actual time gap
- **WHEN** new returned columns start after an unvisited time interval
- **THEN** the heatmap leaves that interval empty
- **AND** it does not extend or duplicate values into the gap

#### Scenario: A late window fills an earlier missing interval
- **WHEN** a returned STFT column arrives after a later timestamp has already been rendered
- **THEN** the heatmap renders that column at its absolute timestamp
- **AND** already-rendered columns are not duplicated
- **AND** other unvisited intervals remain empty
