## 1. Writer and data contract

- [x] 1.1 Add SharpEDFlib and implement bounded EDF+/BDF+ writing from original recording samples, with explicit unit validation, channel metadata, annotations, cancellation, and atomic target completion.
- [x] 1.2 Add focused writer tests that export both formats and reopen them to verify samples, labels, sample rate, start time, and annotations; verify gap and invalid-unit failures leave no target file.
- [x] 1.3 Map overlong or non-ASCII source channel labels to short EDF/BDF aliases and preserve their full names in a companion JSON mapping file.
- [x] 1.4 Verify the user's complete recording with actual EDF and BDF write/read-back tests, including total sample count, duration, channel count, and alias mappings.

## 2. Review workflow

- [x] 2.1 Bind the review export action to an EDF/BDF save dialog and export the complete recording without blocking the UI; show actionable failures.
- [x] 2.2 Run desktop tests/build, strict OpenSpec validation, and `git diff --check`; record platform limits if native writer verification is unavailable.
