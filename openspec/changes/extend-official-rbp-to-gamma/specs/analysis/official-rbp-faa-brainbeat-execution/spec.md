## MODIFIED Requirements

### Requirement: Execute official RBP as one five-band result

The system SHALL execute the current official `rbp` on one explicitly selected raw channel in static or dynamic mode and return Delta 1–4, Theta 4–8, Alpha 8–13, Beta 13–30 and Gamma 30–50 Hz relative powers from the same quality-gated 1–50 Hz PSD. Each power SHALL use boundary-aware integration. The denominator SHALL be the sum of all five powers. The source PSD SHALL declare its reference, 1–50 Hz bandpass, 50 Hz notch (Q=30), Welch settings, frequency grid and units. Because this notch attenuates frequencies near Gamma's 50 Hz boundary, the result SHALL be identified as filtered Gamma power and its exact preprocessing SHALL remain in provenance. Nyquist frequency MUST exceed 50 Hz. The current five-band Run SHALL use scientific version `official-rbp-v2`, implementation identity `rbp-runtime-v2-five-band` and published Definition version `2.0.1`, preserving immutable four-band historical Runs and Definition `1.0.0`.

#### Scenario: Static RBP result

- **WHEN** a clean valid recording, channel and static range are submitted for current `rbp`
- **THEN** the Run returns five finite band shares in ratio units summing to approximately one, their band powers, source channel, actual range, quality and spectral evidence
- **AND THEN** the frontend renders the backend values without recalculating or renormalizing them

#### Scenario: Dynamic RBP result

- **WHEN** a dynamic RBP Run produces a clean window
- **THEN** that window returns five backend-computed shares and its actual time bounds
- **AND THEN** rejected or unavailable windows contain five null values, a structured reason and no carried-over measurement

#### Scenario: RBP quality or frequency failure

- **WHEN** a window fails quality, any band denominator is invalid, or the recording Nyquist frequency cannot cover 50 Hz
- **THEN** the system reports a structured failure without representing unavailable shares as zero or silently reverting to four bands
