## ADDED Requirements

### Requirement: Isolate algorithm spectral preprocessing from display filters

Official spectral algorithms SHALL receive analysis preprocessing settings through their algorithm request/configuration contract. Review waveform filter controls SHALL NOT modify algorithm inputs, preprocessing, run identity, cache identity, or result provenance. The shared spectral baseline SHALL cover 1–50 Hz with a default 50 Hz notch, while each algorithm retains its declared analysis window, Welch segmentation, target frequencies, bands, and quality rules. Compatible user-facing algorithm configurations SHALL permit an explicit notch-off choice.

#### Scenario: Display filter changes during algorithm analysis
- **WHEN** the user changes review waveform filters and submits an official spectral run
- **THEN** the run uses its algorithm-owned preprocessing configuration
- **AND** the display filter values do not appear in its request or provenance.

#### Scenario: Compare notch-on and notch-off runs
- **WHEN** a user executes the same compatible algorithm and range once with default notch and once with notch disabled
- **THEN** each run records its own effective preprocessing settings and distinct run identity
- **AND** each preserves the algorithm's window, Welch parameters, target range, and quality policy.

#### Scenario: Preserve algorithm-specific analysis window
- **WHEN** two spectral algorithms use different declared window lengths or Welch parameters
- **THEN** the shared spectral service computes each request with its declared values
- **AND** the shared preprocessing baseline does not overwrite either configuration.
