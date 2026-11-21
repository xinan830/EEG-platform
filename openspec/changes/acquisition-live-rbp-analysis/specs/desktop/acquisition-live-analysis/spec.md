# Spec Delta

## Purpose

Defines how acquisition sessions run optional live analysis and present its temporary results without coupling analysis to raw capture, display filtering, or offline review.

## ADDED Requirements

### Requirement: Run live analysis as an isolated acquisition branch
The acquisition client SHALL offer the RBP live-analysis branch during preview and recording. The branch SHALL consume accepted raw batches only after raw persistence and SHALL NOT block device reads, raw persistence, or waveform display when its queue is full, its worker is slow, or its worker fails.

#### Scenario: Preview runs RBP
- **WHEN** a user enables RBP during device preview
- **THEN** the live-analysis branch processes available valid EEG samples and publishes temporary results while preview continues

#### Scenario: Recording continues under analysis overload
- **WHEN** the live-analysis queue is full or its worker fails during recording
- **THEN** device acquisition, raw persistence, and waveform display continue
- **AND** the analysis status reports overload or failure instead of presenting stale values as current

#### Scenario: Vendor callbacks contain small sample blocks
- **WHEN** contiguous device callbacks arrive faster than the live HTTP path should be called
- **THEN** the desktop combines them into bounded fixed-duration analysis blocks before transport
- **AND** sample-counter gaps and session boundaries remain explicit and stopping flushes the final partial block

### Requirement: Keep live analysis separate from display filtering
The RBP live-analysis input SHALL come from accepted raw EEG data and its own declared scientific preprocessing. It SHALL NOT consume display-filter output or change raw data, display-filter state, or waveform values.

#### Scenario: Display filter changes during live analysis
- **WHEN** the user changes waveform display filtering while RBP is running
- **THEN** the display branch applies its existing boundary semantics
- **AND** RBP input and results remain governed by the independent analysis preprocessing contract

### Requirement: Keep live results temporary to the acquisition session
The acquisition UI SHALL present live RBP results with their time range, channel order, units, quality, and current/unavailable state. Results SHALL remain session-local, SHALL NOT create or update a persisted Analysis Run, and SHALL be cleared when that acquisition session ends.

#### Scenario: Session ends
- **WHEN** preview or recording stops or the acquisition session faults
- **THEN** the UI stops accepting results for that session and clears its live RBP results
- **AND** no live result is added to offline review or algorithm backtrace data

#### Scenario: A newer result is unavailable
- **WHEN** a live window has a gap, insufficient contiguous samples, or a channel-level quality failure
- **THEN** the UI marks the affected result unavailable or degraded with its structured quality reason
- **AND** it does not keep showing an older value as if it represented the latest window

#### Scenario: Live RBP uses the review chart presentation
- **WHEN** valid live RBP windows arrive during preview or recording
- **THEN** the acquisition panel presents per-channel band history with the same RBP chart control and visual styling used by review
- **AND** each chart point uses the live result's sample-aligned window end time and retains unavailable or degraded quality as a gap/status
- **AND** the acquisition panel does not bind to offline review playback or Run-result coordination

### Requirement: Live RBP exposes the review topomap presentation
The acquisition RBP panel SHALL provide the same stacked-history and EEG topomap choices as the review RBP panel. The live topomap branch SHALL publish the five Delta, Theta, Alpha, Beta and Gamma fields for relative and absolute power from the same sample-aligned window.

#### Scenario: Operator opens the RBP topomap
- **WHEN** the operator selects “脑电地形图” inside the live RBP panel
- **THEN** the panel displays the five backend-produced band maps in the review two-column layout
- **AND** the operator can switch between relative or absolute power and all bands or one band
- **AND** the topomap window range matches the live RBP window range
