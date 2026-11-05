# Validation Record

Date: 2026-11-05

- The feature-focused desktop suite passed 47 tests, as recorded during implementation.
- After the montage-switch frame-retention fix, `RecordingReviewSessionTests` passed 13/13 using an isolated output directory.
- The WPF project compiled as part of the focused desktop test build. The build reports the existing .NET preview SDK and NU1701 package compatibility warnings.
- `openspec validate --specs` passed 86/86 main specs; strict validation passed for this change.
- The user confirmed manual montage-switch testing after the frame-retention fix.
- The full test suite was not run. An earlier broader Review filter recorded 95 passed and 2 failed (`Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel` and `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`). Baseline behavior for those failures was not verified.
