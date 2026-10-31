## Scheduling contract

- [x] Remove active-work cancellation from latest-only coordination.
- [x] Keep TNK playback target coalescing limited to pending work.
- [x] Remove the obsolete caller flag and its cancellation-only regression.

## Verification

- [x] Run focused coordinator and observer tests.
- [x] Validate the change with OpenSpec strict mode and `git diff --check`.
- [ ] Repeat a TNK playback run and confirm no new `dynamic_window_cancelled`
  entries occur when only the cursor advances.
