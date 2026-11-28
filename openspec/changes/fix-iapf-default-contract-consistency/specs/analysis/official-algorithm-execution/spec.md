## MODIFIED Requirements

### Requirement: Official IAPF and Theta/Beta dynamic results require complete analysis windows
Official IAPF and Theta/Beta dynamic policies SHALL default to a 20-second analysis window. IAPF SHALL default to a two-second refresh step and preserve explicit supported steps including one second; Theta/Beta SHALL retain its one-second default. They SHALL support the declared 20- and 30-second windows and SHALL NOT publish numeric warm-up values. Complete windows SHALL use shared four-second Hann Welch segments with 50% overlap and the shared quality gate.

#### Scenario: Emit complete TBR windows every second
- **WHEN** a dynamic Theta/Beta run starts at 0 seconds and reaches 20, 21, and 22 seconds
- **THEN** it analyzes 0–20, 1–21, and 2–22 seconds respectively
- **AND** no numeric Theta/Beta value is emitted before the first complete 20-second window
- **AND** Theta/Beta windows begin at the configured analysis start and advance by the selected step

#### Scenario: Emit the first complete IAPF window
- **WHEN** dynamic playback reaches 20 seconds from its configured start
- **THEN** IAPF is calculated from the 0–20 second window using its nine complete Welch segments
- **AND** no IAPF numeric value is published for earlier partial windows

#### Scenario: Advance complete IAPF windows every two seconds by default
- **WHEN** a dynamic IAPF request omits window duration and refresh step over a 0–30 second recording
- **THEN** it emits six complete windows from 0–20 through 10–30 seconds, advancing by two seconds
- **AND** persisted configuration and execution snapshots record 20-second windows and two-second refresh steps

#### Scenario: Advance complete windows every second when selected
- **WHEN** an IAPF request explicitly selects a one-second step and playback reaches 21 and 22 seconds
- **THEN** it uses windows 1–21 and 2–22 seconds respectively
- **AND** persisted parameters preserve the explicit step

#### Scenario: Average only quality-accepted Welch segments
- **WHEN** one or more Welch segments fail quality checks but the window still meets the shared minimum clean-segment ratio
- **THEN** only accepted segment PSDs are averaged before IAPF estimation
- **AND** when the ratio is below the shared threshold, the window is rejected with a null value

#### Scenario: Use the same window policy in Official Run and on-demand execution
- **WHEN** Official Run and on-demand requests omit TBR dynamic window settings
- **THEN** both resolve to the 20-second window and one-second step declared by the TBR module
- **AND** unsupported shorter windows are rejected with a structured configuration error

## ADDED Requirements

### Requirement: IAPF live and review display ranges remain independent of scientific search
Live and review IAPF charts SHALL remain fixed at 8–13 Hz regardless of incoming values. Scientific search SHALL remain 7–13 Hz and estimates SHALL retain their original Hz values. Numeric results outside the visible axis SHALL remain readable. Changing display mode SHALL NOT recompute results or alter time coordinates, channel order, quality, provenance or Peak/COG fallback semantics.

#### Scenario: Display a low Alpha estimate without rescaling
- **WHEN** a valid 7.5 Hz IAPF estimate arrives in a live or review chart
- **THEN** the chart remains at 8–13 Hz and the original 7.5 Hz result remains numerically available

#### Scenario: Keep live cadence explicit
- **WHEN** live IAPF omits its step or explicitly selects one second
- **THEN** actual sample windows advance by two seconds or one second respectively
- **AND** provenance declares the resolved step, 50% Welch overlap and two-second internal Welch step
