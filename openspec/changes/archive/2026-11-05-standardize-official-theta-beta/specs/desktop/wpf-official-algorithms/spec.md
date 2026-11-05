## ADDED Requirements

### Requirement: WPF selects and submits the Theta/Beta frequency mode
WPF SHALL expose Standard and IAF-relative frequency modes for Official Theta/Beta, default to Standard, and submit the selected mode in the scientific Run configuration. WPF SHALL display the backend-returned mode, effective bands, and IAF when used; it SHALL NOT calculate IAPF, band powers, or the ratio.

#### Scenario: Submit standard mode
- **WHEN** the operator selects Standard Theta/Beta and submits an Official Run
- **THEN** the request contains `frequency_mode=standard`
- **AND** the result displays the backend-returned 4–8 Hz and 13–30 Hz bands

#### Scenario: Submit individualized mode
- **WHEN** the operator selects IAF-relative Theta/Beta and submits an Official Run
- **THEN** the request contains `frequency_mode=individualized`
- **AND** the result displays the backend-returned IAF and effective frequency bands
