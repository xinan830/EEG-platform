## 1. Contract and Implementation

- [x] Extend the local live filter session request and service with optional
  `notch_hz`.
- [x] Restrict notch selection to disabled, 50 Hz, and 60 Hz; validate Nyquist
  constraints before stream opening.
- [x] Bind the desktop control and preserve the existing raw-recording path.

## 2. Verification

- [x] Add a 50 Hz numeric regression test demonstrating suppression in the
  returned display stream and unchanged input data.
- [x] Run focused backend tests, desktop tests, Release build, diff check, and
  strict OpenSpec validation.
