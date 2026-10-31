# Validation Record

Date: 2026-10-31

## Automated checks

- Focused TNK/group suite: 62 passed, 1 skipped.
- OpenSpec strict validation: passed.
- `git diff --check`: passed; Git reported only working-copy line-ending normalization warnings.
- The skipped frozen Welch reference check requires NumPy 2.2.6, SciPy 1.15.3, and specparam 2.0.0rc6. The current environment has NumPy 2.5.4, SciPy 1.18.1, and specparam 2.0.0rc7, so that pinned reference hash was not verified here.

## Remaining acceptance

Real-recording playback responsiveness and behavior remain unverified manually. These engineering checks do not establish clinical validity.
