## ADDED Requirements

### Requirement: Delegate format writing to the backend
The desktop SHALL request complete raw-recording export from the Python backend, independently of analysis registration and current viewing montage. It SHALL retain the EDF/BDF save dialog, show exporting/completed/failed status, allow cancellation, stage signal and metadata files before replacing destinations and restore existing destinations if publication fails. Closing the review SHALL cancel its export. Format writing SHALL have a single backend implementation.

#### Scenario: Export without opening analysis
- **WHEN** the user selects EDF or BDF export before opening an algorithm
- **THEN** the desktop registers the raw source explicitly and saves the backend-generated complete recording and sidecar at the selected destination.

#### Scenario: Cancel export
- **WHEN** the user cancels an export or closes the review
- **THEN** incomplete temporary files are removed and existing destination files remain intact.
