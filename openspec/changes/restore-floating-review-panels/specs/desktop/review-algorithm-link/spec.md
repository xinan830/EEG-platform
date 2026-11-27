# Spec Delta

## ADDED Requirements

### Requirement: Floating Review analysis panels recover after temporary unload
Review SHALL restore one visible floating window for each analysis slot that remains marked floating when its dock host is loaded again. Temporarily unloading the host SHALL NOT close the underlying analysis slot.

#### Scenario: Reload the Review dock host
- **WHEN** a Review dock host with an open floating analysis panel is unloaded and later loaded again
- **THEN** the same analysis slot SHALL have one visible floating window owned by the Review window
- **AND** repeated load synchronization SHALL NOT create duplicate windows.
