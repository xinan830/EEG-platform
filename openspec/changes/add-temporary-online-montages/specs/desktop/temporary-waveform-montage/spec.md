# Temporary waveform montage

## Purpose
Allow users to inspect a complete custom montage in acquisition or recording review through the existing full editor while preserving recordings and independently selected algorithm inputs.

## ADDED Requirements

### Requirement: Complete session editor
The system SHALL open full 导联设置 from 在线导联 with current-session sources, configuration name, reference controls, output labels, colors and order. Save SHALL validate a complete temporary profile without writing the montage library.

#### Scenario: Save configured profile
- **WHEN** the user edits and names a full configuration
- **THEN** saving returns a session-only profile with all configured references, colors and output order

#### Scenario: Invalid or cancelled edits
- **WHEN** the user cancels or attempts an invalid source/reference mapping
- **THEN** cancellation preserves the current display and invalid save shows an error without activation

### Requirement: Whole waveform selection
The system SHALL add the saved temporary profile to 显示导联 and automatically select it, replacing the complete waveform montage. The user SHALL be able to select an ordinary profile and return to the temporary one.

#### Scenario: Save and switch back
- **WHEN** a temporary profile is saved in acquisition or review
- **THEN** its name is selected and all waveform outputs use its definitions, colors and order
- **AND** selecting an ordinary profile restores that profile

### Requirement: Recording and analysis isolation
Temporary configuration SHALL affect only waveform projection, preserving raw samples, V units, sample counters, absolute seconds, quality gaps, recording metadata and algorithm montage selection/revision. Temporary definitions and display levels SHALL NOT be persisted.

#### Scenario: Edit during acquisition or analysis
- **WHEN** a temporary profile is created, replaced or selected
- **THEN** raw recording and algorithm montage inputs remain unchanged

### Requirement: Session lifecycle and stale frames
The system SHALL clear temporary profiles on acquisition stop/new session or review disposal. Profile changes SHALL invalidate obsolete waveform requests and projected caches without clearing original-data caches.

#### Scenario: Change while loading
- **WHEN** a temporary profile changes during a pending waveform request
- **THEN** the obsolete result cannot replace the current display

#### Scenario: End session
- **WHEN** acquisition ends or review is disposed
- **THEN** its temporary configuration and selection entry disappear
